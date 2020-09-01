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
        delegate bool CollectDetectedBlocks(MyDetectedEntityInfo blk);

        T FindFirstBlockOfType<T>(Func<T, bool> collect) where T : class, IMyTerminalBlock
        {
            var blocks = new List<T>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => collect(blk));
            return blocks.Count() > 0 ? blocks[0] : null;
        }

        float RemainingBatteryCapacity(List<IMyBatteryBlock> batteries)
        {
            float totalStoredPower = 0; float totalMaxStoredPower = 0;
            foreach (var battery in batteries)
            {
                totalStoredPower += battery.CurrentStoredPower;
                totalMaxStoredPower += battery.MaxStoredPower;
            }

            return totalStoredPower / totalMaxStoredPower;
        }

        bool CollectSmallGrid(MyDetectedEntityInfo blk)
        {
            return blk.Type == MyDetectedEntityType.SmallGrid;
        }

        bool CollectAll(MyDetectedEntityInfo blk)
        {
            return true;
        }

        bool CollectSameConstruct(IMyTerminalBlock block)
        {
            return block.IsSameConstructAs(Me);
        }

        void EnableBlocks(Func<IMyTerminalBlock, bool> collect)
        {
            var blocks = new List<IMyFunctionalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => collect(blk));
            foreach (var blk in blocks)
            {
                blk.Enabled = true;
            }
        }

        void DisableBlocks(Func<IMyTerminalBlock, bool> collect)
        {
            var blocks = new List<IMyFunctionalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => collect(blk));
            foreach (var blk in blocks)
            {
                blk.Enabled = false;
            }
        }

        int SortByStoredPower(IMyBatteryBlock b1, IMyBatteryBlock b2)
        {
            return b1.CurrentStoredPower.CompareTo(b2.CurrentStoredPower);
        }

        /// <summary>
        /// Thrown when we detect that we have taken up too much processing time
        /// and need to put off the rest of the exection until the next call.
        /// </summary>
        class PutOffExecutionException : Exception { }

        class LoggerContainer
        {

            readonly Action<string> EchoR = text => { };

            List<StringWrapper> _logs = new List<StringWrapper>();

            public LoggerContainer(Program program)
            {
                EchoR = program.EchoR;
            }

            public StringWrapper GetLog(int index)
            {
                if (_logs.Count() <= index)
                {
                    AddNewLog();
                    GetLog(index);
                }
                return _logs[index].Clean();
            }

            public StringWrapper AddNewLog()
            {
                StringWrapper stringWrapper = new StringWrapper();
                _logs.Add(stringWrapper);
                return stringWrapper;
            }

            public void Print() {
                var value = string.Join("\n", _logs.FindAll(log => log.HasText()));
                if (value != "") EchoR(value);
            }
        }

        class StringWrapper
        {
            string _text;

            public StringWrapper Append(string value)
            {
                if (_text != "") _text += "\n";
                _text += value;
                return this;
            }

            public StringWrapper Clean()
            {
                _text = "";
                return this;
            }

            public bool HasText()
            {
                return _text != "";
            }

            public override string ToString()
            {
                return _text;
            }
        }
    }
    
}
