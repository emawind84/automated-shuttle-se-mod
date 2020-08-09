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

        /// <summary>
        /// Start the script with automatic execution
        /// </summary>
        void Start()
        {
            if (USE_REAL_TIME)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update1;
            }
            else
            {
                Runtime.UpdateFrequency = FREQUENCY;
            }
            isRunning = true;
        }

        /// <summary>
        /// Pause the script without altering the current step, use start to resume the process
        /// </summary>
        void Stop()
        {
            isRunning = false;
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        /// <summary>
        /// Reset all the behaviours altered by the script and shut down the script.
        /// </summary>
        void Shutdown()
        {
            ZeroThrustOverride();
            ResetBatteryMode();
            ResetAutopilot();
            isRunning = false;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            EchoR("System shut down");
        }

        /// <summary>
        /// Reset process from step 0
        /// </summary>
        void Reset()
        {
            processStep = 0;
            EchoR("System reset");
        }

        /// <summary>
        /// For debugging, it is possible to execute single step, even though steps should be executed in sequence
        /// </summary>
        void ExecuteStep()
        {
            var step = int.Parse(_commandLine.Argument(1));
            processStep = step;
            isRunning = false;
            Runtime.UpdateFrequency = UpdateFrequency.Once;
        }

        void NextStep()
        {
            processStep++;
            Runtime.UpdateFrequency = UpdateFrequency.Once;
        }

        void AddWaypoint()
        {
            var waypointData = int.Parse(_commandLine.Argument(1)).ToString();
            MyWaypointInfo waypointInfo;
            if (MyWaypointInfo.TryParse(waypointData, out waypointInfo))
            {
                var w = new Waypoint(waypointInfo.Name, waypointInfo.Coords);
                waypoints.Add(w);
                EchoR($"New waypoint added: {w}");
            } else
            {
                EchoR("Waypoint not recognized");
            }
        }

        void NextWaypoint()
        {
            currentWaypoint = GetNextWaypoint();
            processStep = 0;
        }

        void Test()
        {

            EchoR(string.Format("### {0}", ReferenceBlock.WorldMatrix.Up == (Vector3D.Zero - ReferenceBlock.WorldMatrix.Down)));

            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

    }
}
