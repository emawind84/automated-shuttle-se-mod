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

        void SkipIfUndocked()
        {
            // check if the ship is connected to a grid
            if (DockingConnector.Status == MyShipConnectorStatus.Unconnected)
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
                Sensor.RightExtend = 50;
                Sensor.LeftExtend = 50;
                Sensor.FrontExtend = 50;
                Sensor.BackExtend = 50;
                Sensor.BottomExtend = 50;
                Sensor.TopExtend = 50;
            }
        }

        List<MyDetectedEntityInfo> FindDetectedGrids()
        {
            double distanceFromLastPosition = Vector3D.Distance(lastShipPosition, Me.GetPosition());
            var entities = new List<MyDetectedEntityInfo>();

            if (Sensor != null)
            {
                if (distanceFromLastPosition < Sensor.MaxRange / 2)
                {
                    Sensor.DetectedEntities(entities);
                }
                
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

            string customDataWaypoints = _ini.Get("shuttle", "waypoints").ToString();
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

        void ZeroThrustOverride()
        {
            var thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrusters);
            thrusters.ForEach(thruster => thruster.ThrustOverridePercentage = 0);
        }

        void ResetBatteryMode()
        {
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, battery => MyIni.HasSection(battery.CustomData, "shuttle"));
            batteries.ForEach(battery => battery.ChargeMode = ChargeMode.Auto);
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

        void Start()
        {
            Runtime.UpdateFrequency = FREQUENCY;
        }

        void Stop()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        void Shutdown()
        {
            ZeroThrustOverride();
            EchoR("Reset thrust override");
            ResetBatteryMode();
            EchoR("Reset battery charge mode");
            EchoR("System shut down");
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        void Reset()
        {
            processStep = 0;
            EchoR("System reset");
        }

        void Test()
        {
            var entities = FindDetectedGrids().FindAll(grid => grid.Type == MyDetectedEntityType.LargeGrid);
            EchoR(string.Format("Found #{0} entities", entities.Count()));

            foreach (var entity in entities)
            {
                EchoR(string.Format("position {0}", entity.Position));
                EchoR(string.Format("Distance from ship: {0}m", Vector3D.Distance(lastShipPosition, entity.Position)));

                var dir = Vector3D.Normalize(entity.Position - DockingConnector.GetPosition());
                var dot = Vector3D.Dot(dir, DockingConnector.WorldMatrix.Forward);
                var radians = Math.Acos(MathHelper.Clamp(dot, -1f, 1f));
                var degrees = MathHelper.ToDegrees(radians);
                EchoR("dot: " + dot);
                EchoR("grad: " + degrees);

                // between 90 - 180 obstructed
            }

            Runtime.UpdateFrequency = UpdateFrequency.None;
        }
    }
}
