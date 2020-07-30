﻿using Sandbox.Game.EntityComponents;
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
                previousStepEndTime = DateTime.Now;
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

        public delegate bool CollectBlocks(MyDetectedEntityInfo blk);

        void SkipIfObstructed(Vector3D directionalVector, CollectBlocks collect = null)
        {
            if (IsObstructed(directionalVector, collect))
            {
                EchoR("Skipping: obstructed");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void PrepareSensor()
        {
            if (Sensor != null)
            {
                Sensor.Enabled = true;
                Sensor.DetectFriendly = true;
                Sensor.DetectOwner = true;
                Sensor.DetectStations = true;
                Sensor.DetectLargeShips = true;
                Sensor.DetectSubgrids = true;
                Sensor.DetectPlayers = false;
                Sensor.RightExtend = 50;
                Sensor.LeftExtend = 50;
                Sensor.FrontExtend = 50;
                Sensor.BackExtend = 50;
                Sensor.BottomExtend = 50;
                Sensor.TopExtend = 50;
            }
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
            var currentWaypointName = Storage;
            if (currentWaypointName != null)
            {
                currentWaypoint = waypoints.Find(waypoint => waypoint.Name == currentWaypointName);
            }
        }

        void RetrieveCustomSetting()
        {
            // init settings
            _ini.TryParse(Me.CustomData);

            var parkingPeriodInSeconds = _ini.Get(ScriptPrefixTag, "ParkingPeriod").ToInt16(10);
            parkingPeriodAtWaypoint = new TimeSpan(0, 0, parkingPeriodInSeconds);

            string customDataWaypoints = _ini.Get(ScriptPrefixTag, "Waypoints").ToString();
            if (customDataWaypoints != "")
            {
                string[] _waypoints = customDataWaypoints.Split(',');
                for (int i = 0; i < _waypoints.Count(); i++)
                {
                    waypoints.Add(new Waypoint(_waypoints[i]));
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
                yield return displayTerminals.Run();
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

        bool IsObstructed(Vector3D directionalVector, CollectBlocks collect = null)
        {
            if (collect == null)
            {
                collect = CollectAll;
            }
            var entities = FindDetectedGrids().FindAll(blk => collect(blk));

            foreach (var entity in entities)
            {
                var dir = Vector3D.Normalize(entity.Position - ReferenceBlock.GetPosition());
                var dot = Vector3D.Dot(dir, Vector3D.Normalize(directionalVector));
                var radians = Math.Acos(MathHelper.Clamp(dot, -1f, 1f));
                var degrees = MathHelper.ToDegrees(radians);

                if (degrees > 0 && degrees < 65)
                {
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
            GridTerminalSystem.GetBlocksOfType(batteries, battery => MyIni.HasSection(battery.CustomData, ScriptPrefixTag));
            batteries.ForEach(battery => battery.ChargeMode = ChargeMode.Auto);
        }

        void ResetAutopilot()
        {
            RemoteControl.ClearWaypoints();
            RemoteControl.SetAutoPilotEnabled(false);
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

        IEnumerator<bool> SetSubProcessStepsCycle()
        {
            var log = new LoggerContainer(this);
            while (true) {
                yield return CheckRemainingBatteryCapacity(log.GetLog(0));
                log.Print();
                yield return DoSomeOtherCheck(log.GetLog(1));
                log.Print();
            }
        }

        bool CheckRemainingBatteryCapacity(StringWrapper log) {
            var powerProducers = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(powerProducers, CollectSameConstruct);
            float remainingCapacity = RemainingBatteryCapacity(powerProducers);
            if (remainingCapacity < CriticalBatteryCapacity && !criticalBatteryCapacityDetected) {
                criticalBatteryCapacityDetected = true;
                var timerblocks = new List<IMyTimerBlock>();
                GridTerminalSystem.GetBlocksOfType<IMyTimerBlock>(timerblocks, tb => MyIni.HasSection(tb.CustomData, ScriptPrefixTag + ":CriticalCurrentDetected"));
                timerblocks.ForEach(tb => tb.Trigger());
            }
            else if (remainingCapacity > CriticalBatteryCapacity && criticalBatteryCapacityDetected)
            {
                criticalBatteryCapacityDetected = false;
                var timerblocks = new List<IMyTimerBlock>();
                GridTerminalSystem.GetBlocksOfType<IMyTimerBlock>(timerblocks, tb => MyIni.HasSection(tb.CustomData, ScriptPrefixTag + ":NormalCurrentReestablished"));
                timerblocks.ForEach(tb => tb.Trigger());
            }

            log.Append(string.Format("Battery capacity: {0}%", Math.Round(remainingCapacity * 100, 0)));
            return true;
        }

        bool DoSomeOtherCheck(StringWrapper log)
        {
            log.Append("other check...");
            log.Append("other check 222...");
            return true;
        }

        
    }
}
