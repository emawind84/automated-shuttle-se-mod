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
        float RemainingBatteryCapacity(List<IMyBatteryBlock> batteries)
        {
            float totalCurrentCapacity = 0; float totalMaxCapacity = 0;
            for (int i = 0; i < batteries.Count(); i++)
            {
                totalCurrentCapacity += batteries[i].CurrentStoredPower;
                totalMaxCapacity += batteries[i].MaxStoredPower;
            }

            return totalCurrentCapacity / totalMaxCapacity;
        }

        bool CollectSmallGrid(MyDetectedEntityInfo blk)
        {
            return blk.Type == MyDetectedEntityType.SmallGrid;
        }

        bool CollectAll(MyDetectedEntityInfo blk)
        {
            return true;
        }

        bool CollectSameGrid(IMyTerminalBlock block)
        {
            return block.CubeGrid == Me.CubeGrid;
        }

        /// <summary>
        /// Thrown when we detect that we have taken up too much processing time
        /// and need to put off the rest of the exection until the next call.
        /// </summary>
        class PutOffExecutionException : Exception { }
    }
    
}
