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
        class Waypoint
        {

            public string Name { get; }

            public Vector3D Coords { get; }

            public bool WaitAtWaypoint { get; }

            public Waypoint(string name, Vector3D coords, bool waitAtWaypoint)
            {
                this.Coords = coords;
                this.WaitAtWaypoint = waitAtWaypoint;
                this.Name = name;
            }

            public Waypoint(string waypointData)
            {
                //GPS: WAYPOINT 1:227004.83:227029.62:227020.38:
                MyWaypointInfo waypointInfo;
                if (MyWaypointInfo.TryParse(waypointData, out waypointInfo))
                {
                    this.Name = waypointInfo.Name;
                    this.Coords = waypointInfo.Coords;
                    this.WaitAtWaypoint = !waypointData.Contains(":nowait");
                }
                else
                {
                    throw new PutOffExecutionException();
                }
            }
        }
    }
}
