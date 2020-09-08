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
            var connector = DockingConnector;
            if (connector != null)
            {
                // check if the ship is connected to a grid
                if (connector.Status == MyShipConnectorStatus.Connected
                    || connector.Status == MyShipConnectorStatus.Connectable)
                {
                    EchoR("Skipping: ship docked");
                    processStep++;
                    throw new PutOffExecutionException();
                }
            }
        }

        void SkipIfNotConnected()
        {
            var connector = DockingConnector;
            
            // check if the ship is connected to a grid
            if (connector == null || 
                connector.Status == MyShipConnectorStatus.Unconnected ||
                connector.Status == MyShipConnectorStatus.Connectable)
            {
                EchoR("Skipping: ship undocked");
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void SkipIfDockingConnectorAbsent()
        {
            if (DockingConnector == null)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        void RunIfStopAtWaypointEnabled()
        {
            if (!currentWaypoint.StopAtWaypoint)
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

        void SkipIfOrbitMode()
        {
            if (OrbitMode)
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

        void PrepareSensor(IMySensorBlock _sensor)
        {
            if (_sensor != null)
            {
                _sensor.Enabled = true;
                _sensor.DetectFriendly = true;
                _sensor.DetectOwner = true;
                _sensor.DetectEnemy = true;
                _sensor.DetectStations = true;
                _sensor.DetectLargeShips = true;
                _sensor.DetectSmallShips = true;
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

        void PrepareConnector(IMyShipConnector connector)
        {
            // bug fix connector too strong
            if (connector != null)
            {
                connector.PullStrength = 0;
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

            ParkingPeriodAtWaypoint = TimeSpan.FromSeconds(_ini.Get(ScriptPrefixTag, "ParkingPeriod").ToInt16(10));
            ManageBattery = _ini.Get(ScriptPrefixTag, "ManageBattery").ToBoolean(true);

            #region Waypoints Settings
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
            #endregion

            #region Orbit Settings
            OrbitMode = _ini.Get(ScriptPrefixTag, "OrbitMode").ToBoolean(false);
            EchoR($"OrbitMode: {OrbitMode}");
            if (OrbitMode)
            {
                OrbitRadius = _ini.Get(ScriptPrefixTag, "OrbitRadius").ToDouble(DefaultOrbitRadius);
                if (OrbitRadius < 100) OrbitRadius = DefaultOrbitRadius;
                EchoR($"OrbitRadius: {OrbitRadius}");
                string orbitCenterPositionUserData = _ini.Get(ScriptPrefixTag, "OrbitCenterPosition").ToString();
                MyWaypointInfo _tmp;
                if (MyWaypointInfo.TryParse(orbitCenterPositionUserData, out _tmp))
                    OrbitCenterPosition = _tmp.Coords;
                EchoR($"OrbitCenterPosition: {OrbitCenterPosition}");
            }
            #endregion
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
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk));
            batteries.ForEach(battery => battery.ChargeMode = ChargeMode.Auto);
        }

        void ResetAutopilot()
        {
            var _rc = RemoteControl;
            _rc?.ClearWaypoints();
            _rc?.SetAutoPilotEnabled(false);
        }

        Waypoint FindNextOrbitWaypoint()
        {
            double planetRadius = OrbitRadius;

            var xDirectionalVector = new Vector3D(1, 0, 0);
            var yDirectionalVector = new Vector3D(0, 1, 0);
            var entityToPlanetDirectionalVector = Vector3D.Normalize(ReferenceBlock.GetPosition() - OrbitCenterPosition);

            var dotX = Vector3D.Dot(xDirectionalVector, entityToPlanetDirectionalVector);
            var dotY = Vector3D.Dot(yDirectionalVector, entityToPlanetDirectionalVector);

            var entityXYAngle = Math.Acos(MathHelper.Clamp(dotX, -1f, 1f));
            if (dotY < 0)
            {
                entityXYAngle = 2 * Math.PI - entityXYAngle;
            }
            entityXYAngle += (Math.PI * 2) / 20;  // next waypoint increment

            var z = 0;
            var x = planetRadius * Math.Cos(entityXYAngle);
            var y = planetRadius * Math.Sin(entityXYAngle);

            if (orbitYZAngle == 0)
                orbitYZAngle = CalculateYZAngle();
            MatrixD xRotationMatrix = new MatrixD(1, 0, 0, 0, Math.Cos(orbitYZAngle), -Math.Sin(orbitYZAngle), 0, Math.Sin(orbitYZAngle), Math.Cos(orbitYZAngle));
            var gpsCoords = new Vector3D(x, y, z);
            gpsCoords = Vector3D.Rotate(gpsCoords, xRotationMatrix);
            gpsCoords = Vector3D.Add(gpsCoords, OrbitCenterPosition);

            //EchoR($"GPS:NextWP:{gpsCoords.X}:{gpsCoords.Y}:{gpsCoords.Z}:#FFF17575:");

            return new Waypoint(string.Format("WP:{0:#.###}", entityXYAngle), gpsCoords, false);
        }

        double CalculateYZAngle()
        {
            var yDirectionalVector = new Vector3D(0, 1, 0);
            var entityToPlanetDirectionalVector = Vector3D.Normalize(ReferenceBlock.GetPosition() - OrbitCenterPosition);
            var entityAngle = Math.Atan2(entityToPlanetDirectionalVector.Y, entityToPlanetDirectionalVector.Z)
                - Math.Atan2(yDirectionalVector.Y, yDirectionalVector.Z);
            if (entityAngle < 0)
            {
                entityAngle += 2 * Math.PI;
            }
            if (entityAngle > (Math.PI / 2) && entityAngle < (Math.PI * 3 / 2))
            {
                entityAngle += Math.PI;
            }
            return entityAngle;
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
        
    }
}
