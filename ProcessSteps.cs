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
        #region Process Steps

        void ResetControl()
        {
            var cockpits = new List<IMyCockpit>();
            GridTerminalSystem.GetBlocksOfType(cockpits);
            cockpits.ForEach(cockpit => {
                cockpit.ControlThrusters = true;
            });

            ResetBatteryMode();
            ZeroThrustOverride();

            processStep++;
        }

        void FindNextWaypoint()
        {
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

            processStep++;
        }

        void RechargeBatteries()
        {
            SkipIfUndocked();

            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, battery => MyIni.HasSection(battery.CustomData, "shuttle"));
            //EchoR(string.Format("Found #{0} batteries", batteries.Count()));
            if (batteries.Count() == 0)
            {
                processStep++;
                throw new PutOffExecutionException();
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

                processStep++;
            }
        }

        void DoBeforeUndocking()
        {
            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, "shuttle:beforeundocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            processStep++;
        }

        void UndockShip()
        {
            DockingConnector.Disconnect();
            DockingConnector.PullStrength = 0;

            if (DockingConnector.Status != MyShipConnectorStatus.Connected)
            {
                processStep++;
            }
        }

        void MoveAwayFromDock()
        {
            SkipIfUndocked();

            var thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.Orientation.Forward == DockingConnector.Orientation.Forward);
            double currentSpeed = RemoteControl.GetShipSpeed();
            double distanceFromDock = Vector3D.Distance(lastShipPosition, Me.GetPosition());

            if (distanceFromDock < SafeDistanceFromDock && currentSpeed < 5)
            {
                thrusters.ForEach(thrust =>
                {
                    thrust.ThrustOverridePercentage += 0.1f;
                });
            }
            else if (distanceFromDock > SafeDistanceFromDock)
            {
                processStep++;
            }

        }

        void ResetThrustOverride()
        {
            ZeroThrustOverride();

            processStep++;
        }

        void GoToWaypoint()
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

            processStep++;
        }

        void TravelToWaypoint()
        {
            SkipIfDocked();

            double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, Me.GetPosition());
            if (Math.Round(RemoteControl.GetShipSpeed(), 0) == 0 && distanceFromWaypoint < 100)
            {
                processStep++;
            }
        }

        void DockToStation()
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
            if (programmableBlock.IsRunning) { return; }
            programmableBlock.TryRun(currentWaypoint.Name);

            processStep++;
        }

        void WaitDockingCompletion()
        {
            SkipIfNoGridNearby();
            SkipOnTimeout(30);

            if (DockingConnector.Status == MyShipConnectorStatus.Connectable
                || DockingConnector.Status == MyShipConnectorStatus.Connected)
            {

                processStep++;
            }
        }

        void DoAfterDocking()
        {
            SkipIfNoGridNearby();

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, "shuttle:afterdocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            processStep++;
        }

        void WaitAtWaypoint()
        {
            SkipIfNoGridNearby();
            DateTime n = DateTime.Now;
            if (n - previousStepEndTime >= parkingPeriodAtWaypoint)
            {
                processStep++;
            }
        }

        #endregion
    }
}
