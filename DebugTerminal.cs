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
        /// Defines the <see cref="DebugTerminal" />.
        /// </summary>
        class DebugTerminal : Terminal<IMyTerminalBlock>
        {
            readonly Func<IMyTerminalBlock, bool> collect;

            /// <summary>
            /// Initializes a new instance of the <see cref="DebugTerminal"/> class.
            /// </summary>
            /// <param name="program">The program<see cref="Program"/>.</param>
            /// <param name="map">The map<see cref="Map"/>.</param>
            public DebugTerminal(Program program, Func<IMyTerminalBlock, bool> collect = null) : base(program)
            {
                if (collect != null)
                {
                    this.collect = collect;
                } else
                {
                    this.collect = blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag);
                }
            }

            /// <summary>
            /// The OnCycle.
            /// </summary>
            /// <param name="lcd">The lcd<see cref="IMyTextPanel"/>.</param>
            public override void OnCycle(IMyTerminalBlock block)
            {
                base.OnCycle(block);

                MyIni ini = new MyIni();
                ini.TryParse(block.CustomData);
                var display = ini.Get(ScriptPrefixTag, "display").ToInt16();

                IMyTextSurface lcd;
                if (block is IMyTextSurfaceProvider)
                {
                    lcd = (block as IMyTextSurfaceProvider).GetSurface(display);
                }
                else
                {
                    lcd = block as IMyTextPanel;
                }

                lcd.ContentType = ContentType.TEXT_AND_IMAGE;
                lcd.WriteText(program.echoOutput);
            }

            /// <summary>
            /// The Collect.
            /// </summary>
            /// <param name="terminal">The terminal<see cref="IMyTextPanel"/>.</param>
            /// <returns>The <see cref="bool"/>.</returns>
            public override bool Collect(IMyTerminalBlock terminal)
            {
                //program.EchoR(string.Format("Collecting {0}", terminal.CustomName));
                
                // Collect this.
                bool isValidBlock = collect(terminal)
                    && terminal.IsSameConstructAs(program.Me)
                    && (terminal is IMyTextPanel || terminal is IMyTextSurfaceProvider)
                    && terminal.IsWorking;

                return isValidBlock;
            }
            
        }
    }
}
