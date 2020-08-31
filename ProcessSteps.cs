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
            //ResetAutopilot();
            //ZeroThrustOverride();
            PrepareSensor();

            criticalBatteryCapacityDetected = false;
            lowBatteryCapacityDetected = false;

            processStep++;
        }

        void ProcessStepFindNextWaypoint()
        {
            SkipIfCurrentWaypointTooFar();

            currentWaypoint = new Waypoint("O", FindNextOrbitWaypoint(), false);
            
            //if (waypoints.Count() == 0)
            //{
            //    EchoR("No waypoint defined");
            //    throw new PutOffExecutionException();
            //}
            //if (currentWaypoint == null)
            //{
            //    currentWaypoint = waypoints.First();
            //}
            //else
            //{
            //    double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, ReferenceBlock.GetPosition());

            //    if (distanceFromWaypoint < 100)
            //    {
            //        currentWaypoint = GetNextWaypoint();
            //    }

            //}

            processStep++;
        }

        void ProcessStepRechargeBatteries()
        {
            SkipIfOrbitMode();
            RunEveryCycles(2);
            SkipIfNotConnected();

            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsFunctional);
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
            SkipIfOrbitMode();
            SkipIfNoGridNearby();
            SkipOnTimeout(10);

            informationTerminals.Text = "Doors are closing";
        }

        void ProcessStepDoBeforeUndocking()
        {
            SkipIfOrbitMode();
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
            SkipIfOrbitMode();
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
            SkipIfOrbitMode();
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
            if (currentWaypoint.Coords == controlBlock.CurrentWaypoint.Coords)
            {
                processStep++;
                throw new PutOffExecutionException();
            }

            controlBlock.SetCollisionAvoidance(true);
            controlBlock.FlightMode = FlightMode.OneWay;
            controlBlock.SetDockingMode(false);
            if (currentWaypoint.StopAtWaypoint) controlBlock.SetDockingMode(true);

            float distanceToNextWaypoint = Vector3.Distance(ReferenceBlock.GetPosition(), currentWaypoint.Coords);
            if (distanceToNextWaypoint > 50)
            {
                //controlBlock.ClearWaypoints();
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
            RunIfStopAtWaypoint();

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
            SkipIfOrbitMode();
            SkipIfDocked();
            //SkipIfNoGridNearby(); if the ship is too far from the grid this step is not goind to be executed

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
            SkipIfOrbitMode();
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
            SkipIfOrbitMode();
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
            SkipIfOrbitMode();
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
            SkipIfOrbitMode();
            var _dc = DockingConnector;
            if (_dc.Status == MyShipConnectorStatus.Connectable)
            {
                _dc.Connect();
            }
            processStep++;
        }

        void ProcessStepWaitAtWaypoint()
        {
            if (currentWaypoint.StopAtWaypoint)
            {
                DateTime n = DateTime.Now;
                if (n - previousStepEndTime >= parkingPeriodAtWaypoint)
                {
                    processStep++;
                }

                TimeSpan remainingSeconds = parkingPeriodAtWaypoint - (n - previousStepEndTime);
                informationTerminals.Text = string.Format("Departure for {0} in {1}s", GetNextWaypoint()?.Name, Math.Round(remainingSeconds.TotalSeconds, 0));
            }
            else
            {
                processStep++;
            }
        }

        void ProcessStepWaitUndefinetely()
        {
            //RunEveryCycles(2);
            //var velocity = RemoteControl.GetShipVelocities().LinearVelocity;
            //EchoR("" + velocity);
            //var directionalVector = Vector3D.Normalize(lastShipPosition - ReferenceBlock.GetPosition());
            //EchoR("" + directionalVector);
            //var obstructed = IsObstructed(velocity, blk => blk.Type == MyDetectedEntityType.SmallGrid);
            //EchoR("Obstructed: " + obstructed);

            FindNextOrbitWaypoint();
        }

        private Vector3D FindNextOrbitWaypoint()
        {
            Vector3D planetCenterPosition = new Vector3D(0, 0, 0);
            double planetRadius = 50000;

            var xDirectionalVector = new Vector3D(1, 0, 0);
            var shipPlanetDirectionalVector = Vector3D.Normalize(ReferenceBlock.GetPosition() - planetCenterPosition);
            
            var entityXYAngle = Math.Atan2(shipPlanetDirectionalVector.Y, shipPlanetDirectionalVector.X) 
                - Math.Atan2(xDirectionalVector.Y, xDirectionalVector.X);

            if (entityXYAngle < 0) entityXYAngle += 2 * Math.PI;
            entityXYAngle += (Math.PI * 2) / 20;  // next waypoint increment
            EchoR("Rad: " + entityXYAngle);
            EchoR("Next position XY: " + MathHelper.ToDegrees(entityXYAngle));

            var z = 0;
            var x = planetRadius * Math.Cos(entityXYAngle);
            var y = planetRadius * Math.Sin(entityXYAngle);
            var gpsCoords = new Vector3D(x, y, z);

            var yDirectionalVector = new Vector3D(0, 1, 0);
            var entityToPlanetDirectionalVector = Vector3D.Normalize(ReferenceBlock.GetPosition() - planetCenterPosition);
            var dot = Vector3D.Dot(yDirectionalVector, entityToPlanetDirectionalVector);
            var entityAngle = Math.Acos(MathHelper.Clamp(dot, -1f, 1f));
            var degrees = MathHelper.ToDegrees(entityAngle);
            EchoR("angle " + degrees);
            //var entityAngle = Math.Atan2(entityToPlanetDirectionalVector.Y, entityToPlanetDirectionalVector.Z)
            //    - Math.Atan2(yDirectionalVector.Y, yDirectionalVector.Z);
            //if (entityAngle < 0) entityAngle += 2 * Math.PI;
            MatrixD xRotationMatrix = new MatrixD(1, 0, 0, 0, Math.Cos(entityAngle), -Math.Sin(entityAngle), 0, Math.Sin(entityAngle), Math.Cos(entityAngle));
            
            gpsCoords = Vector3D.Rotate(gpsCoords, xRotationMatrix);
            gpsCoords = Vector3D.Add(gpsCoords, planetCenterPosition);

            EchoR($"GPS:NextWP:{gpsCoords.X}:{gpsCoords.Y}:{gpsCoords.Z}:#FFF17575:");

            return gpsCoords;
        }

        #endregion
    }
}
