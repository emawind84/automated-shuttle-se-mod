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
    /// <summary>
    /// Common behavior to all terminals.
    /// </summary>
    /// <typeparam name="T">.</typeparam>
    abstract class Terminal<T> where T : class, IMyTerminalBlock
    {
        /// <summary>
        /// Log for Terminal class
        /// </summary>
        public Action<string> EchoR = text => { };
        /// <summary>
        /// Defines the program.
        /// </summary>
        protected Program Program;

        /// <summary>
        /// Defines the list.
        /// </summary>
        private readonly List<T> list = new List<T>();

        /// <summary>
        /// Defines the listIndex, listUpdate...
        /// </summary>
        private int listIndex, listUpdate;

        /// <summary>
        /// Initializes a new instance of the <see cref="Terminal{T}"/> class.
        /// </summary>
        /// <param name="program">The program<see cref="IngameScript.Program"/>.</param>
        public Terminal(Program program)
        {
            this.Program = program;
            //this.EchoR = program.EchoR;
        }

        /// <summary>
        /// Gets a value indicating whether IsListEmpty.
        /// </summary>
        public bool IsListEmpty => list.Count == 0;

        /// <summary>
        /// The Run.
        /// </summary>
        /// <returns>The <see cref="bool"/>.</returns>
        public bool Run()
        {
            if (!IsListEmpty)
            {
                listIndex = listIndex % list.Count;
                EchoR(string.Format("Collected #{1} {0} ", this.GetType().Name, list.Count, listIndex));

                if (!IsCorrupt(list[listIndex]))
                {
                    EchoR(string.Format("Cycling block #{0}", listIndex));
                    OnCycle(list[listIndex]);
                }
                else
                {
                    list.Remove(list[listIndex]);
                }
                listIndex++;
            }

            if (listUpdate % 10 == 0)
            {
                EchoR("Updating collection");
                Program.GridTerminalSystem.GetBlocksOfType(list, Collect);
                listUpdate = 0;
            }

            listUpdate++;

            return true;
        }

        /// <summary>
        /// Specificies which terminals to be collected. Use override.
        /// </summary>
        /// <param name="terminal">The terminal<see cref="T"/>.</param>
        /// <returns>The <see cref="bool"/>.</returns>
        public virtual bool Collect(T terminal)
        {
            return terminal.IsWorking;
        }

        /// <summary>
        /// The OnCycle.
        /// </summary>
        /// <param name="terminal">The terminal<see cref="T"/>.</param>
        public virtual void OnCycle(T terminal)
        {
            EchoR(string.Format("Cycling on {0}", terminal.CustomName));
        }

        /// <summary>
        /// Checks if the terminal is null, gone from world, or broken off from grid.
        /// </summary>
        /// <param name="block">The block<see cref="T"/>.</param>
        /// <returns>The <see cref="bool"/>.</returns>
        public bool IsCorrupt(T block)
        {
            bool isCorrupt = false;
            isCorrupt |= block == null || block.WorldMatrix == MatrixD.Identity;
            isCorrupt |= !(Program.GridTerminalSystem.GetBlockWithId(block.EntityId) == block);

            return isCorrupt;
        }
    }
}
