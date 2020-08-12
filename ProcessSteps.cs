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
            ResetBatteryMode();
            ResetAutopilot();
            ZeroThrustOverride();
            PrepareSensor();

            criticalBatteryCapacityDetected = false;
            lowBatteryCapacityDetected = false;

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
                    currentWaypoint = GetNextWaypoint();
                }

            }

            processStep++;
        }

        void ProcessStepRechargeBatteries()
        {
            RunEveryCycles(2);
            SkipIfNotConnected();

            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsWorking);
            batteries.Sort(SortByStoredPower);
            
            if (batteries.Count() == 0)
            {
                processStep++;
                throw new PutOffExecutionException();
            }

            float remainingCapacity = RemainingBatteryCapacity(batteries);

            if (remainingCapacity < MinBatteryCapacity
                || (remainingCapacity < ChargedBatteryCapacity && lowBatteryCapacityDetected))
            {
                lowBatteryCapacityDetected = true;
                var batteriesToCharge = Convert.ToInt16(batteries.Count / 2 + 0.5f);
                foreach (var battery in batteries.Skip(batteriesToCharge))
                {
                    battery.ChargeMode = ChargeMode.Auto;
                }
                foreach (var battery in batteries.Take(batteriesToCharge)) {
                    battery.ChargeMode = ChargeMode.Recharge;
                }

                EchoR(string.Format("Charging batteries: {0}%", Math.Round(remainingCapacity * 100, 0)));
                informationTerminals.Text = string.Format("Charging batteries: {0}%", Math.Round(remainingCapacity * 100, 0));
            }
            else
            {
                lowBatteryCapacityDetected = false;
                foreach (var battery in batteries)
                {
                    battery.ChargeMode = ChargeMode.Auto;
                }

                processStep++;
            }
        }

        void ProcessStepWaitBeforeUndocking()
        {
            SkipIfNoGridNearby();
            SkipOnTimeout(10);

            informationTerminals.Text = "Doors are closing";
        }

        void ProcessStepDoBeforeUndocking()
        {
            SkipIfNoGridNearby();

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, blk => MyIni.HasSection(blk.CustomData, TriggerBeforeUndockingTag) && CollectSameConstruct(blk));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            var blocks = new List<IMyFunctionalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, ToggleBeforeUndockingTag) && CollectSameConstruct(blk));
            blocks.ForEach(blk => blk.Enabled = !blk.Enabled);

            processStep++;
        }

        void ProcessStepUndockShip()
        {
            SkipIfNoGridNearby();

            var _dc = DockingConnector;
            _dc.Disconnect();
            _dc.PullStrength = 0;
            if (_dc.Status != MyShipConnectorStatus.Connected)
            {
                processStep++;
            }
        }

        void ProcessStepMoveAwayFromDock()
        {
            SkipIfNoGridNearby();
            //SkipIfNoSensor();

            var _dc = DockingConnector;
            var thrusters = new List<IMyThrust>();
            if (!IsObstructed(_dc.WorldMatrix.Backward))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == _dc.WorldMatrix.Forward);
            }
            else if (!IsObstructed(_dc.WorldMatrix.Forward))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == _dc.WorldMatrix.Backward);
            }
            else if (!IsObstructed(_dc.WorldMatrix.Left))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == _dc.WorldMatrix.Right);
            }
            else if (!IsObstructed(_dc.WorldMatrix.Right))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == _dc.WorldMatrix.Left);
            }
            else if (!IsObstructed(_dc.WorldMatrix.Up))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == _dc.WorldMatrix.Down);
            }
            else if (!IsObstructed(_dc.WorldMatrix.Down))
            {
                GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.WorldMatrix.Forward == _dc.WorldMatrix.Up);
            }
            else
            {
                ZeroThrustOverride();
                EchoR("Ship is obstructed, waiting clearance");
                throw new PutOffExecutionException();
            }

            double currentSpeed = RemoteControl.GetShipSpeed();
            double distanceFromDock = Vector3D.Distance(lastShipPosition, ReferenceBlock.GetPosition());

            if (distanceFromDock < SafeDistanceFromDock && currentSpeed < 5)
            {
                thrusters.ForEach(thrust =>
                {
                    if (thrust.CurrentThrust > thrust.ThrustOverride)
                    {
                        thrust.ThrustOverride = thrust.CurrentThrust + 5000f;
                    }
                    thrust.ThrustOverride += 2000f;
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

            var controlBlock = RemoteControl;
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
            GridTerminalSystem.GetBlocksOfType(beacons, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            foreach (IMyBeacon beacon in beacons)
            {
                beacon.Enabled = false;
            }

            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType(antennas, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
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
            GridTerminalSystem.GetBlocksOfType(beacons, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            foreach (IMyBeacon beacon in beacons)
            {
                beacon.Enabled = true;
            }

            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType(antennas, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
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
            var _rc = RemoteControl;
            double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, ReferenceBlock.GetPosition());
            if (Math.Round(_rc.GetShipSpeed(), 0) == 0 && distanceFromWaypoint < 100)
            {
                _rc.SetAutoPilotEnabled(false);  // might be still enabled and we want it disabled before the next step
                processStep++;
            }

            informationTerminals.Text = string.Format("Arriving at {0} in {1}s", currentWaypoint.Name, Math.Round(distanceFromWaypoint / _rc.GetShipSpeed(), 0));
        }

        void ProcessStepDockToStation()
        {
            //SkipIfNoGridNearby(); if the ship is too far from the grid this step is not goind to be executed
            SkipIfDocked();

            // start docking
            var dockingScript = FindFirstBlockOfType<IMyProgrammableBlock>(blk => MyIni.HasSection(blk.CustomData, DockingScriptTag) && blk.IsWorking);
            if (dockingScript == null)
            {
                EchoR("Docking script not found");
                processStep++;
                throw new PutOffExecutionException();
            }
            if (dockingScript.IsRunning) {
                EchoR("Docking script already running");
                processStep++;
                throw new PutOffExecutionException();
            }
            if (IsObstructed(DockingConnector.WorldMatrix.Forward, CollectSmallGrid))
            {
                EchoR("Path obstructed, waiting for docking");
                throw new PutOffExecutionException();
            }

            dockingScript.TryRun(currentWaypoint.Name);
            processStep++;
        }

        void ProcessStepWaitDockingCompletion()
        {
            //SkipIfNoGridNearby();
            SkipOnTimeout(30);
            var _dc = DockingConnector;
            if (_dc.Status == MyShipConnectorStatus.Connectable || _dc.Status == MyShipConnectorStatus.Connected)
            {
                processStep++;
            }

            informationTerminals.Text = string.Format("Docking at {0}", currentWaypoint.Name);
        }

        void ProcessStepDoAfterDocking()
        {
            //SkipIfNoGridNearby();  // if the ship is connected the station is not counted

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, blk => MyIni.HasSection(blk.CustomData, TriggerAfterDockingTag) && CollectSameConstruct(blk));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            var blocks = new List<IMyFunctionalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, ToggleAfterDockingTag) && CollectSameConstruct(blk));
            blocks.ForEach(blk => blk.Enabled = !blk.Enabled);

            processStep++;
        }

        void ProcessStepDisconnectConnector()
        {
            var _dc = DockingConnector;
            _dc.Disconnect();
            if (_dc.Status == MyShipConnectorStatus.Connected)
            {
                EchoR("Connector still connected");
                throw new PutOffExecutionException();
            }
            processStep++;
        }

        void ProcessStepConnectConnector()
        {
            var _dc = DockingConnector;
            if (_dc.Status == MyShipConnectorStatus.Connectable)
            {
                _dc.Connect();
            }
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

            TimeSpan remainingSeconds = parkingPeriodAtWaypoint - (n - previousStepEndTime);
            informationTerminals.Text = string.Format("Departure for {0} in {1}s", GetNextWaypoint()?.Name, Math.Round(remainingSeconds.TotalSeconds, 0));
        }

        void ProcessStepWaitUndefinetely()
        {
            RunEveryCycles(2);

            EchoR("Running step now!");
        }

        #endregion
    }
}
