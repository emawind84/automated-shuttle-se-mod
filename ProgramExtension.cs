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

        void SkipOnTimeout(int seconds)
        {
            if (currentCycleStartTime - previousStepEndTime > new TimeSpan(0, 0, seconds))
            {
                EchoR("Skipping on timeout");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfDocked()
        {
            // check if the ship is connected to a grid
            if (DockingConnector.Status == MyShipConnectorStatus.Connected
                || DockingConnector.Status == MyShipConnectorStatus.Connectable)
            {
                EchoR("Skipping: ship docked");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfNotConnected()
        {
            // check if the ship is connected to a grid
            if (DockingConnector.Status == MyShipConnectorStatus.Unconnected
                || DockingConnector.Status == MyShipConnectorStatus.Connectable)
            {
                EchoR("Skipping: ship undocked");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfWaypointWithoutDock()
        {
            if (!currentWaypoint.WaitAtWaypoint)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfNoGridNearby()
        {
            if (Sensor == null) return;

            if (!IsLargeGridNearby())
            {
                EchoR("Skipping: no grids nearby");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfObstructed(Vector3D directionalVector, CollectDetectedBlocks collect = null)
        {
            if (IsObstructed(directionalVector, collect))
            {
                EchoR("Skipping: obstructed");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfNoSensor()
        {
            if (Sensor == null)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void RunEveryCycles(int cycles)
        {
            if (DateTime.Now - previousStepEndTime > TimeSpan.FromMilliseconds(100) && totalCallCount % cycles != 0)
            {
                throw new PutOffExecutionException();
            }
        }

        void PrepareSensor()
        {
            var _sensor = Sensor;
            if (_sensor != null)
            {
                _sensor.Enabled = true;
                _sensor.DetectFriendly = true;
                _sensor.DetectOwner = true;
                _sensor.DetectEnemy = true;
                _sensor.DetectStations = true;
                _sensor.DetectLargeShips = true;
                _sensor.DetectAsteroids = true;
                _sensor.DetectSubgrids = false;  // we don't want to detect grids connected with rotors or connectors
                _sensor.DetectPlayers = false;
                //_sensor.RightExtend = 25;
                //_sensor.LeftExtend = 25;
                //_sensor.FrontExtend = 25;
                //_sensor.BackExtend = 25;
                //_sensor.BottomExtend = 25;
                //_sensor.TopExtend = 25;
            }
        }

        Waypoint GetNextWaypoint()
        {
            if (waypoints.Count() == 0) return null;
            int totalWaypoints = waypoints.Count();
            int index = waypoints.FindIndex(wp => wp.Name == currentWaypoint.Name);
            return waypoints[(index + 1) % totalWaypoints];
        }

        void UpdateLastShipPosition()
        {
            lastShipPosition = ReferenceBlock.GetPosition();
        }

        List<MyDetectedEntityInfo> FindDetectedGrids()
        {
            var entities = new List<MyDetectedEntityInfo>();
            Sensor?.DetectedEntities(entities);
            if (Sensor == null)
            {
                EchoR("No sensor registered");
            }
            return entities;
        }

        bool IsLargeGridNearby()
        {
            var entities = FindDetectedGrids().FindAll(grid => grid.Type == MyDetectedEntityType.LargeGrid);
            return entities.Count() > 0;
        }

        void RetrieveStorage()
        {
            string[] storedData = Storage.Split(';');
            if (storedData.Length >= 1)
            {
                currentWaypoint = waypoints.Find(waypoint => waypoint.Name == storedData[0]);
                //EchoR(string.Format("Retrieved waypoint: {0}", currentWaypoint?.Name));
            }
            if (storedData.Length >= 2)
            {
                bool.TryParse(storedData[1], out isRunning);
                //EchoR(string.Format("Script state: {0}", isRunning ? "Running" : "Stopped"));
            }
        }

        void RetrieveCustomSetting()
        {
            // init settings
            _ini.TryParse(Me.CustomData);

            var parkingPeriodInSeconds = _ini.Get(ScriptPrefixTag, "ParkingPeriod").ToInt16(10);
            parkingPeriodAtWaypoint = TimeSpan.FromSeconds(parkingPeriodInSeconds);
            
            string customDataWaypoints = _ini.Get(ScriptPrefixTag, "Waypoints").ToString();
            if (customDataWaypoints != "")
            {
                string[] _waypoints = customDataWaypoints.Split(',');
                foreach (string waypointData in _waypoints)
                {
                    MyWaypointInfo waypointInfo;
                    if (MyWaypointInfo.TryParse(waypointData, out waypointInfo))
                    {
                        var w = new Waypoint(waypointInfo.Name, waypointInfo.Coords);
                        waypoints.Add(w);
                    }
                }
            }
        }

        /// <summary>
        /// The SetTerminalCycle.
        /// </summary>
        /// <returns>The <see cref="IEnumerator{bool}"/>.</returns>
        IEnumerator<bool> SetTerminalCycle()
        {
            while (true)
            {
                yield return debugTerminals.Run();
                yield return informationTerminals.Run();
            }
        }

        /// <summary>
        /// Checks if the terminal is null, gone from world, or broken off from grid.
        /// </summary>
        /// <param name="block">The block<see cref="T"/>.</param>
        /// <returns>The <see cref="bool"/>.</returns>
        bool IsCorrupt(IMyTerminalBlock block)
        {
            bool isCorrupt = block == null || block.WorldMatrix == MatrixD.Identity
                || !(GridTerminalSystem.GetBlockWithId(block.EntityId) == block);

            return isCorrupt;
        }

        bool IsObstructed(Vector3D directionalVector, CollectDetectedBlocks collect = null)
        {
            if (collect == null)
            {
                collect = CollectAll;
            }
            var entities = FindDetectedGrids().FindAll(blk => collect(blk));
            var referenceBlock = ReferenceBlock;

            foreach (var entity in entities)
            {
                var dir = Vector3D.Normalize(entity.Position - referenceBlock.GetPosition());
                var dot = Vector3D.Dot(dir, Vector3D.Normalize(directionalVector));
                var radians = Math.Acos(MathHelper.Clamp(dot, -1f, 1f));
                var degrees = MathHelper.ToDegrees(radians);
                
                if (degrees > 0 && degrees < 65)
                {
                    //EchoR(string.Format("Grid detected: {0}, degrees {1}, type: {2}", entity.Name, degrees, entity.Type));
                    return true;
                }
            }
            return false;
        }

        void ZeroThrustOverride()
        {
            var thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrusters);
            thrusters.ForEach(thruster => thruster.ThrustOverridePercentage = 0);
        }

        void ResetBatteryMode()
        {
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsWorking);
            batteries.ForEach(battery => battery.ChargeMode = ChargeMode.Auto);
        }

        void ResetAutopilot()
        {
            var _rc = RemoteControl;
            _rc?.ClearWaypoints();
            _rc?.SetAutoPilotEnabled(false);
        }

        /// <summary>
        /// Checks if the current call has exceeded the maximum execution limit.
        /// If it has, then it will raise a <see cref="PutOffExecutionException:T"/>.
        /// </summary>
        /// <returns>True.</returns>
        /// <remarks>This methods returns true by default to allow use in the while check.</remarks>
        bool DoExecutionLimitCheck()
        {
            if (ExecutionTime > MAX_RUN_TIME || ExecutionLoad > MAX_LOAD)
                throw new PutOffExecutionException();
            return true;
        }

        IEnumerator<bool> SetSubProcessStepCycle()
        {
            var loggerContainer = new LoggerContainer(this);
            while (true) {
                yield return SubProcessCheckRemainingBatteryCapacity(loggerContainer.GetLog(0));
                loggerContainer.Print();
                yield return SubProcessActivateEmergencyPower(loggerContainer.GetLog(1));
                loggerContainer.Print();
                yield return SubProcessSendBroadcastMessage();
                loggerContainer.Print();
            }
        }

        bool SubProcessCheckRemainingBatteryCapacity(StringWrapper log) {
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsWorking);
            if (batteries.Count() > 0)
            {
                float remainingCapacity = RemainingBatteryCapacity(batteries);
                if (remainingCapacity < CriticalBatteryCapacity)
                {
                    log.Append("Critical power detected");
                    criticalBatteryCapacityDetected = true;
                    var timerblocks = new List<IMyTimerBlock>();
                    GridTerminalSystem.GetBlocksOfType(timerblocks, tb => MyIni.HasSection(tb.CustomData, TriggerOnCriticalCurrentDetectedTag));
                    timerblocks.ForEach(tb => tb.Trigger());

                    // disable blocks with DisableOnEmergencyTag
                    EnableBlocks(blk => MyIni.HasSection(blk.CustomData, DisableOnEmergencyTag), false);
                }
                else
                {
                    criticalBatteryCapacityDetected = false;
                    var timerblocks = new List<IMyTimerBlock>();
                    GridTerminalSystem.GetBlocksOfType(timerblocks, tb => MyIni.HasSection(tb.CustomData, TriggerOnNormalCurrentReestablishedTag));
                    timerblocks.ForEach(tb => tb.Trigger());

                    // enable blocks with DisableOnEmergencyTag
                    EnableBlocks(blk => MyIni.HasSection(blk.CustomData, DisableOnEmergencyTag));
                }

                log.Append(string.Format("Battery capacity: {0}%", Math.Round(remainingCapacity * 100, 0)));
            }
            return true;
        }

        bool SubProcessActivateEmergencyPower(StringWrapper log)
        {
            var generators = new List<IMyPowerProducer>();
            GridTerminalSystem.GetBlocksOfType(generators, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, EmergencyPowerTag));

            if (criticalBatteryCapacityDetected)
            {
                log.Append("Emergency power on");
                generators.ForEach(blk => blk.Enabled = true);
            }
            else
            {
                generators.ForEach(blk => blk.Enabled = false);
            }
            return true;
        }

        bool SubProcessDoSomeOtherCheck(StringWrapper log)
        {
            log.Append("other check...");
            log.Append("other check 222...");
            return true;
        }

        bool SubProcessEnableBroadcasting()
        {
            var antenna = FindFirstBlockOfType<IMyRadioAntenna>(blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            antenna.Enabled = true;
            antenna.EnableBroadcasting = true;

            return true;
        }

        bool SubProcessDisableBroadcasting()
        {
            var antenna = FindFirstBlockOfType<IMyRadioAntenna>(blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            antenna.EnableBroadcasting = false;

            return true;
        }

        bool SubProcessSendBroadcastMessage()
        {
            var message = MyTuple.Create
            (
                Me.CubeGrid.EntityId,
                Me.CubeGrid.CustomName,
                lastShipPosition,
                informationTerminals.Text
            );
            
            IGC.SendBroadcastMessage(StateBroadcastTag, message);

            return true;
        }
        
    }
}
