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
            if (!IsGridNearby())
            {
                EchoR("Skipping: no grids nearby");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        bool IsGridNearby()
        {
            double distanceFromDock = Vector3D.Distance(lastShipPosition, Me.GetPosition());
            bool gridDetected = false;
            var sensors = new List<IMySensorBlock>();
            GridTerminalSystem.GetBlocksOfType<IMySensorBlock>(sensors, block => MyIni.HasSection(block.CustomData, "shuttle"));
            var sensor = sensors.Find(block => block.IsFunctional && block.IsWorking);
            if (sensor != null)
            {
                if (distanceFromDock > sensor.MaxRange / 2)
                {
                    // the ship already moved from the waypoint and the grid detection might not be attendible
                    return true;
                }
                //sensor.Enabled = true;
                sensor.DetectFriendly = true;
                sensor.DetectOwner = true;
                sensor.DetectStations = true;
                sensor.DetectLargeShips = true;
                sensor.DetectSubgrids = true;
                sensor.RightExtend = 50;
                sensor.LeftExtend = 50;
                sensor.FrontExtend = 50;
                sensor.BackExtend = 50;
                sensor.BottomExtend = 50;
                sensor.TopExtend = 50;
                var entities = new List<MyDetectedEntityInfo>();
                sensor.DetectedEntities(entities);
                foreach (MyDetectedEntityInfo entity in entities)
                {
                    if (entity.Type == MyDetectedEntityType.LargeGrid)
                    {
                        gridDetected = true;
                    }
                }
                //sensor.Enabled = false;
            }
            else
            {
                gridDetected = true;
            }
            return gridDetected;
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
            GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.Orientation.Forward == DockingConnector.Orientation.Forward);
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
    }
}
