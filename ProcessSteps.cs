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

        void ProcessStepResetControl()
        {
            var cockpits = new List<IMyCockpit>();
            GridTerminalSystem.GetBlocksOfType(cockpits, CollectSameConstruct);
            cockpits.ForEach(cockpit => {
                cockpit.ControlThrusters = true;
            });

            ResetBatteryMode();
            ResetAutopilot();
            ZeroThrustOverride();
            PrepareSensor();

            processStep++;
        }

        void ProcessStepFindNextWaypoint()
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
                double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, ReferenceBlock.GetPosition());

                if (distanceFromWaypoint < 100)
                {
                    int totalWaypoints = waypoints.Count();
                    int index = waypoints.FindIndex(wp => wp.Name == currentWaypoint.Name);
                    currentWaypoint = waypoints[(index + 1) % totalWaypoints];
                }

            }

            processStep++;
        }

        void ProcessStepRechargeBatteries()
        {
            SkipIfNotConnected();

            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
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

                foreach (var battery in batteries.SkipWhile(blk => blk.ChargeMode == ChargeMode.Recharge)) {
                    battery.ChargeMode = ChargeMode.Recharge;
                }
            }
            else
            {
                lowBatteryCapacityDetected = false;
                batteries.ForEach(battery => {
                    battery.ChargeMode = ChargeMode.Auto;
                });

                processStep++;
            }

            subProcessStepCycle.MoveNext();
        }

        void ProcessStepDoBeforeUndocking()
        {
            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, ScriptPrefixTag + ":beforeundocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            processStep++;
        }

        void ProcessStepUndockShip()
        {
            DockingConnector.Disconnect();
            DockingConnector.PullStrength = 0;

            if (DockingConnector.Status != MyShipConnectorStatus.Connected)
            {
                processStep++;
            }
        }

        void ProcessStepMoveAwayFromDock()
        {
            SkipIfNoGridNearby();
            
            var thrusters = new List<IMyThrust>();
            if (!IsObstructed(DockingConnector.WorldMatrix.Backward))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == DockingConnector.WorldMatrix.Forward);
            }
            else if (!IsObstructed(DockingConnector.WorldMatrix.Forward))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == DockingConnector.WorldMatrix.Backward);
            }
            else if (!IsObstructed(DockingConnector.WorldMatrix.Left))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == DockingConnector.WorldMatrix.Right);
            }
            else if (!IsObstructed(DockingConnector.WorldMatrix.Right))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == DockingConnector.WorldMatrix.Left);
            }
            else if (!IsObstructed(DockingConnector.WorldMatrix.Up))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == DockingConnector.WorldMatrix.Down);
            }
            else if (!IsObstructed(DockingConnector.WorldMatrix.Down))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == DockingConnector.WorldMatrix.Up);
            }
            else
            {
                EchoR("Ship is obstructed, waiting clearance");
                throw new PutOffExecutionException();
            }

            double currentSpeed = RemoteControl.GetShipSpeed();
            double distanceFromDock = Vector3D.Distance(lastShipPosition, ReferenceBlock.GetPosition());

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

        void ProcessStepResetThrustOverride()
        {
            ZeroThrustOverride();

            processStep++;
        }

        void ProcessStepGoToWaypoint()
        {
            SkipIfDocked();

            IMyRemoteControl controlBlock = RemoteControl;
            controlBlock.SetCollisionAvoidance(true);
            controlBlock.FlightMode = FlightMode.OneWay;
            controlBlock.SetDockingMode(false);
            if (currentWaypoint.WaitAtWaypoint) controlBlock.SetDockingMode(true);

            float distanceToWaypoint = Vector3.Distance(ReferenceBlock.GetPosition(), currentWaypoint.Coords);
            if (distanceToWaypoint > 50)
            {
                controlBlock.ClearWaypoints();
                controlBlock.AddWaypoint(currentWaypoint.Coords, currentWaypoint.Name);
                controlBlock.SetAutoPilotEnabled(true);
            }

            processStep++;
        }

        void ProcessStepDisableBroadcasting()
        {
            var beacons = new List<IMyBeacon>();
            GridTerminalSystem.GetBlocksOfType<IMyBeacon>(beacons, blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            foreach (IMyBeacon beacon in beacons)
            {
                beacon.Enabled = false;
            }

            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(antennas, blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            foreach (var antenna in antennas)
            {
                antenna.Enabled = false;
                antenna.EnableBroadcasting = false;
            }
            processStep++;
        }

        void ProcessStepEnableBroadcasting()
        {
            var beacons = new List<IMyBeacon>();
            GridTerminalSystem.GetBlocksOfType<IMyBeacon>(beacons, blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            foreach (IMyBeacon beacon in beacons)
            {
                beacon.Enabled = true;
            }

            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType<IMyRadioAntenna>(antennas, blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            foreach (var antenna in antennas)
            {
                antenna.Enabled = true;
                antenna.EnableBroadcasting = true;
            }
            processStep++;
        }

        void ProcessStepTravelToWaypoint()
        {
            SkipIfDocked();

            double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, ReferenceBlock.GetPosition());
            if (Math.Round(RemoteControl.GetShipSpeed(), 0) == 0 && distanceFromWaypoint < 100)
            {
                processStep++;
            }

            subProcessStepCycle.MoveNext();
        }

        void ProcessStepDockToStation()
        {
            SkipIfDocked();
            
            // start docking
            List<IMyProgrammableBlock> blocks = new List<IMyProgrammableBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag + ":docking"));
            IMyProgrammableBlock programmableBlock = blocks.Find(block => block.IsFunctional & block.IsWorking);
            if (programmableBlock == null)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
            if (programmableBlock.IsRunning) { return; }

            if (IsObstructed(DockingConnector.WorldMatrix.Forward, CollectSmallGrid))
            {
                // wait until path is clear
                throw new PutOffExecutionException();
            }

            programmableBlock.TryRun(currentWaypoint.Name);
            processStep++;
        }

        void ProcessStepWaitDockingCompletion()
        {
            SkipIfNoGridNearby();
            SkipOnTimeout(30);
            SkipIfObstructed(DockingConnector.WorldMatrix.Forward, CollectSmallGrid);
            
            if (DockingConnector.Status == MyShipConnectorStatus.Connectable
                || DockingConnector.Status == MyShipConnectorStatus.Connected)
            {

                processStep++;
            }
        }

        void ProcessStepDoAfterDocking()
        {
            SkipIfNoGridNearby();

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, ScriptPrefixTag + ":AfterDocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            processStep++;
        }

        void ProcessStepWaitAtWaypoint()
        {
            SkipIfNoGridNearby();
            DateTime n = DateTime.Now;
            if (n - previousStepEndTime >= parkingPeriodAtWaypoint)
            {
                processStep++;
            }

            subProcessStepCycle.MoveNext();
        }

        void ProcessStepWaitUndefinetely()
        {
            subProcessStepCycle.MoveNext();
        }

        #endregion
    }
}
