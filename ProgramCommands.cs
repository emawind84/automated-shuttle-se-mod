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
        }

        /// <summary>
        /// Pause the script without altering the current step, use start to resume the process
        /// </summary>
        void Stop()
        {
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
            try
            {
                var step = int.Parse(_commandLine.Argument(1));
                processStep = step;
                processSteps[processStep]();
                Runtime.UpdateFrequency = UpdateFrequency.None;
            }
            catch (PutOffExecutionException) { }
        }

        void Test()
        {

            EchoR(string.Format("### {0}", ReferenceBlock.WorldMatrix.Up == (Vector3D.Zero - ReferenceBlock.WorldMatrix.Down)));

            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

    }
}
