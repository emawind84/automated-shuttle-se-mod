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

            public Waypoint(string name, Vector3D coords, bool waitAtWaypoint = true)
            {
                this.Coords = coords;
                this.WaitAtWaypoint = waitAtWaypoint;
                this.Name = name;
            }
            
        }
    }
}
