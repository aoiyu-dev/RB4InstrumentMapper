using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RB4InstrumentMapper.Vjoy;
using vJoyInterfaceWrap;

namespace RB4InstrumentMapper.Parsing
{
    class FallbackVjoyMapper : IDeviceMapper
    {
        private vJoy.JoystickState state = new vJoy.JoystickState();
        private uint deviceId = 0;

        /// <summary>
        /// Creates a new VjoyMapper.
        /// </summary>
        public FallbackVjoyMapper()
        {
            deviceId = VjoyClient.GetNextAvailableID();
            if (deviceId == 0)
            {
                throw new VjoyException("No vJoy devices are available.");
            }

            if (!VjoyClient.AcquireDevice(deviceId))
            {
                throw new VjoyException($"Could not claim vJoy device {deviceId}.");
            }

            state.bDevice = (byte)deviceId;
            Console.WriteLine($"Acquired vJoy device with ID of {deviceId}");
        }

        /// <summary>
        /// Performs cleanup on object finalization.
        /// </summary>
        ~FallbackVjoyMapper()
        {
            Dispose(false);
        }

        /// <summary>
        /// Handles an incoming packet.
        /// </summary>
        public void HandlePacket(CommandId command, ReadOnlySpan<byte> data)
        {
            if (deviceId == 0)
                throw new ObjectDisposedException("this");

            switch (command)
            {
                case CommandId.Input:
                    ParseInput(data);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Parses an input report.
        /// </summary>
        public unsafe void ParseInput(ReadOnlySpan<byte> data)
        {
            if (data.Length == sizeof(GuitarInput) && MemoryMarshal.TryRead(data, out GuitarInput guitarReport))
            {
                ParseGuitar(guitarReport);
            }
            else if (data.Length == sizeof(DrumInput) && MemoryMarshal.TryRead(data, out DrumInput drumReport))
            {
                ParseDrums(drumReport);
            }
            else
            {
                // Report is not valid
                return;
            }

            // Send data
            VjoyClient.UpdateDevice(deviceId, ref state);
        }

        /// <summary>
        /// Parses common button data from an input report.
        /// </summary>
        private void ParseCoreButtons(GamepadButton buttons)
        {
            // Menu
            state.SetButton(VjoyButton.Fifteen, (buttons & GamepadButton.Menu) != 0);

            // Options
            state.SetButton(VjoyButton.Sixteen, (buttons & GamepadButton.Options) != 0);

            VjoyPoV direction;
            if ((buttons & GamepadButton.DpadUp) != 0)
            {
                if ((buttons & GamepadButton.DpadLeft) != 0)
                {
                    direction = VjoyPoV.UpLeft;
                }
                else if ((buttons & GamepadButton.DpadRight) != 0)
                {
                    direction = VjoyPoV.UpRight;
                }
                else
                {
                    direction = VjoyPoV.Up;
                }
            }
            else if ((buttons & GamepadButton.DpadDown) != 0)
            {
                if ((buttons & GamepadButton.DpadLeft) != 0)
                {
                    direction = VjoyPoV.DownLeft;
                }
                else if ((buttons & GamepadButton.DpadRight) != 0)
                {
                    direction = VjoyPoV.DownRight;
                }
                else
                {
                    direction = VjoyPoV.Down;
                }
            }
            else
            {
                if ((buttons & GamepadButton.DpadLeft) != 0)
                {
                    direction = VjoyPoV.Left;
                }
                else if ((buttons & GamepadButton.DpadRight) != 0)
                {
                    direction = VjoyPoV.Right;
                }
                else
                {
                    direction = VjoyPoV.Neutral;
                }
            }

            state.bHats = (uint)direction;

            // Other buttons are not mapped here since they may have specific uses
        }

        /// <summary>
        /// Parses guitar input data from an input report.
        /// </summary>
        private void ParseGuitar(GuitarInput report)
        {
            // Buttons
            ParseCoreButtons((GamepadButton)report.Buttons);

            state.SetButton(VjoyButton.One, report.Green);
            state.SetButton(VjoyButton.Two, report.Red);
            state.SetButton(VjoyButton.Three, report.Yellow);
            state.SetButton(VjoyButton.Four, report.Blue);
            state.SetButton(VjoyButton.Five, report.Orange);

            // Whammy
            // Value ranges from 0 (not pressed) to 255 (fully pressed)
            state.AxisY = report.WhammyBar.ScaleToInt32();

            // Tilt
            // Value ranges from 0 to 255
            // It seems to have a threshold of around 0x70 though,
            // after a certain point values will get floored to 0
            state.AxisZ = report.Tilt.ScaleToInt32();

            // Pickup switch
            // Reported values are 0x00, 0x10, 0x20, 0x30, and 0x40 (ranges from 0 to 64)
            state.AxisX = report.PickupSwitch.ScaleToInt32();
        }

        /// <summary>
        /// Parses drums input data from an input report.
        /// </summary>
        private void ParseDrums(DrumInput report)
        {
            // Buttons
            var buttons = (GamepadButton)report.Buttons;
            ParseCoreButtons(buttons);

            // Face buttons
            state.SetButton(VjoyButton.Four, (buttons & GamepadButton.A) != 0);
            state.SetButton(VjoyButton.One, (buttons & GamepadButton.B) != 0);
            state.SetButton(VjoyButton.Three, (buttons & GamepadButton.X) != 0);
            state.SetButton(VjoyButton.Two, (buttons & GamepadButton.Y) != 0);

            // Pads
            state.SetButton(VjoyButton.One, report.RedPad != 0);
            state.SetButton(VjoyButton.Two, report.YellowPad != 0);
            state.SetButton(VjoyButton.Three, report.BluePad != 0);
            state.SetButton(VjoyButton.Four, report.GreenPad != 0);

            // Cymbals
            state.SetButton(VjoyButton.Six, report.YellowCymbal != 0);
            state.SetButton(VjoyButton.Seven, report.BlueCymbal != 0);
            state.SetButton(VjoyButton.Eight, report.GreenCymbal != 0);

            // Kick pedals
            state.SetButton(VjoyButton.Five, (report.Buttons & (ushort)DrumButton.KickOne) != 0);
            state.SetButton(VjoyButton.Nine, (report.Buttons & (ushort)DrumButton.KickTwo) != 0);
        }

        /// <summary>
        /// Performs cleanup for the vJoy mapper.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            // Reset report
            state.Reset();
            VjoyClient.UpdateDevice(deviceId, ref state);

            // Free device
            VjoyClient.ReleaseDevice(deviceId);
            deviceId = 0;
        }
    }
}
