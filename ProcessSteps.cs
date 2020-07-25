using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        class Step : IEnumerator<bool>
        {
            readonly Func<IEnumerator<bool>> stepFunction;

            protected IEnumerator<bool> stepExecutor;

            /// <summary>
            /// Defines the program.
            /// </summary>
            protected Program program;

            public Step(Program program, Func<IEnumerator<bool>> stepFunc)
            {
                stepFunction = stepFunc;
                stepExecutor = stepFunction();
                this.program = program;
            }

            public bool Current { get; set; }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                bool hasMoreSteps = false;
                try
                {
                    hasMoreSteps = stepExecutor.MoveNext();
                    Current = stepExecutor.Current;
                } catch (PutOffExecutionException)
                {
                }

                if (!hasMoreSteps)
                {
                    stepExecutor.Dispose();
                    stepExecutor = stepFunction();

                    program.processStep++;
                }

                return hasMoreSteps;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                throw new NotSupportedException();
            }

        }

        #region Process Steps

        /// <summary>
        /// The SetTerminalCycle.
        /// </summary>
        /// <returns>The <see cref="IEnumerator{bool}"/>.</returns>
        IEnumerator<bool> ProcessCycle()
        {
            var processSteps = new List<Step>()
            {
                new Step(this, ResetControl),
                new Step(this, FindNextWaypoint),
                new Step(this, DoBeforeUndocking)
            };

            EchoR("##### start of ProcessCycle");

            while (true)
            {
                foreach (var step in processSteps)
                {
                    while (step.MoveNext()) yield return true;
                }
                //foreach (int i in DoBeforeUndocking()) { yield return i; }
                //foreach (int i in UndockShip()) { yield return i; }
                //foreach (int i in MoveAwayFromDock()) { yield return i; }
                //foreach (int i in ResetOverrideThruster()) { yield return i; }
                //foreach (int i in GoToWaypoint()) { yield return i; }
                //foreach (int i in TravelToWaypoint()) { yield return i; }
                //foreach (int i in DockToStation()) { yield return i; }
                //foreach (int i in WaitDockingCompletion()) { yield return i; }
                //foreach (int i in DoAfterDocking()) { yield return i; }
                //foreach (int i in RechargeBatteries()) { yield return i; }
                //foreach (int i in WaitAtWaypoint()) { yield return i; }
            }
        }

        IEnumerator<bool> ResetControl()
        {
            yield return true;
            var cockpits = new List<IMyCockpit>();
            GridTerminalSystem.GetBlocksOfType(cockpits);
            yield return true;
            cockpits.ForEach(cockpit => {
                cockpit.ControlThrusters = true;
            });
            yield return true;
            ResetBatteryMode();
            yield return true;
            ZeroThrust();
        }

        IEnumerator<bool> FindNextWaypoint()
        {
            yield return true;
            if (waypoints.Count() == 0)
            {
                EchoR("No waypoint defined");
                throw new PutOffExecutionException();
            }
            if (currentWaypoint == null)
            {
                currentWaypoint = waypoints.First();
            }
            else
            {
                double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, Me.GetPosition());

                if (distanceFromWaypoint < 100)
                {
                    int totalWaypoints = waypoints.Count();
                    int index = waypoints.FindIndex(wp => wp.Name == currentWaypoint.Name);
                    currentWaypoint = waypoints[(index + 1) % totalWaypoints];
                }

            }
        }

        IEnumerator<bool> DoBeforeUndocking()
        {
            yield return true;
            lastDockedPosition = RemoteControl.GetPosition();

            /*
            var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => MyIni.HasSection(door.CustomData, "shuttle"));
            doors.ForEach(door => {
                door.CloseDoor();
                door.Enabled = false;
            });
            */

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, "shuttle:beforeundocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());
        }

        IEnumerable<int> UndockShip()
        {
            DockingConnector.Disconnect();
            DockingConnector.PullStrength = 0;

            if (DockingConnector.Status != MyShipConnectorStatus.Connected)
            {
                yield return processStep++;
            }
        }

        IEnumerable<int> MoveAwayFromDock()
        {
            SkipIfNoGridNearby();

            var thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.Orientation.Forward == DockingConnector.Orientation.Forward);
            double currentSpeed = RemoteControl.GetShipSpeed();
            double distanceFromDock = Vector3D.Distance(lastDockedPosition, Me.GetPosition());

            if (distanceFromDock < SafeDistanceFromDock && currentSpeed < 5)
            {
                thrusters.ForEach(thrust =>
                {
                    thrust.ThrustOverridePercentage += 0.1f;
                });
            }
            else if (distanceFromDock > SafeDistanceFromDock)
            {
                yield return processStep++;
            }

        }

        IEnumerable<int> ResetOverrideThruster()
        {
            ZeroThrust();

            yield return processStep++;
        }

        IEnumerable<int> GoToWaypoint()
        {
            SkipIfDocked();

            IMyRemoteControl controlBlock = RemoteControl;
            controlBlock.SetCollisionAvoidance(true);
            controlBlock.FlightMode = FlightMode.OneWay;
            controlBlock.SetDockingMode(false);
            if (currentWaypoint.WaitAtWaypoint) controlBlock.SetDockingMode(true);

            Vector3 controlBlockPosition = controlBlock.GetPosition();
            float distanceToWaypoint = Vector3.Distance(controlBlockPosition, currentWaypoint.Coords);
            if (distanceToWaypoint > 50)
            {
                controlBlock.ClearWaypoints();
                controlBlock.AddWaypoint(currentWaypoint.Coords, currentWaypoint.Name);
                controlBlock.SetAutoPilotEnabled(true);
            }

            yield return processStep++;
        }

        IEnumerable<int> TravelToWaypoint()
        {
            SkipIfDocked();

            double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, Me.GetPosition());
            if (Math.Round(RemoteControl.GetShipSpeed(), 0) == 0 && distanceFromWaypoint < 100)
            {
                yield return processStep++;
            }
        }

        IEnumerable<int> DockToStation()
        {
            SkipIfDocked();

            // start docking
            List<IMyProgrammableBlock> blocks = new List<IMyProgrammableBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, "shuttle:docking"));
            IMyProgrammableBlock programmableBlock = blocks.Find(block => block.IsFunctional & block.IsWorking);
            if (programmableBlock == null)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
            if (programmableBlock.IsRunning) { yield break; }
            programmableBlock.TryRun(currentWaypoint.Name);

            yield return processStep++;
        }

        IEnumerable<int> WaitDockingCompletion()
        {
            SkipIfNoGridNearby();
            SkipOnTimeout(30);

            if (DockingConnector.Status == MyShipConnectorStatus.Connectable
                || DockingConnector.Status == MyShipConnectorStatus.Connected)
            {

                yield return processStep++;
            }
        }

        IEnumerable<int> DoAfterDocking()
        {
            SkipIfNoGridNearby();

            /*var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => MyIni.HasSection(door.CustomData, "shuttle"));
            doors.ForEach(door => {
                //door.OpenDoor();
                door.Enabled = true;
            });*/

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, "shuttle:afterdocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            yield return processStep++;
        }

        IEnumerable<int> RechargeBatteries()
        {
            SkipIfUndocked();

            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, battery => MyIni.HasSection(battery.CustomData, "shuttle"));
            //EchoR(string.Format("Found #{0} batteries", batteries.Count()));
            if (batteries.Count() == 0)
            {
                processStep++;
                yield break;
            }

            float remainingCapacity = RemainingBatteryCapacity(batteries);

            if (remainingCapacity < MinBatteryCapacity
                || (remainingCapacity < MaxBatteryCapacity && lowBatteryCapacityDetected))
            {
                EchoR(string.Format("Charging batteries: {0}%", Math.Round(remainingCapacity * 100, 0)));
                lowBatteryCapacityDetected = true;

                batteries.ForEach(battery => {
                    if (!battery.IsCharging)
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                    }
                    else
                    {
                        battery.ChargeMode = ChargeMode.Recharge;
                    }
                });
            }
            else
            {
                lowBatteryCapacityDetected = false;
                batteries.ForEach(battery => {
                    battery.ChargeMode = ChargeMode.Auto;
                });

                yield return processStep++;
            }
        }

        IEnumerable<int> WaitAtWaypoint()
        {
            SkipIfNoGridNearby();
            DateTime n = DateTime.Now;
            if (n - previousStepEndTime >= parkingPeriodAtWaypoint)
            {
                yield return processStep++;
            }
        }

        #endregion
    }
}
