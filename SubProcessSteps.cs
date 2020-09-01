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
        IEnumerator<bool> SetSubProcessStepCycle()
        {
            var loggerContainer = new LoggerContainer(this);
            while (true)
            {
                yield return SubProcessCheckRemainingBatteryCapacity(loggerContainer.GetLog(0));
                loggerContainer.Print();
                yield return SubProcessActivateEmergencyPower(loggerContainer.GetLog(1));
                loggerContainer.Print();
                yield return SubProcessSendBroadcastMessage();
                loggerContainer.Print();
            }
        }

        bool SubProcessCheckRemainingBatteryCapacity(StringWrapper log)
        {
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsFunctional);
            if (batteries.Count() > 0)
            {
                float remainingCapacity = RemainingBatteryCapacity(batteries);
                if (remainingCapacity < CriticalBatteryCapacity)
                {
                    log.Append("Critical power detected");
                    criticalBatteryCapacityDetected = true;
                    var timerblocks = new List<IMyTimerBlock>();
                    GridTerminalSystem.GetBlocksOfType(timerblocks, tb => MyIni.HasSection(tb.CustomData, TriggerOnCriticalCurrentDetectedTag));
                    timerblocks.ForEach(tb => tb.Trigger());

                    // disable blocks with DisableOnEmergencyTag
                    EnableBlocks(blk => MyIni.HasSection(blk.CustomData, DisableOnEmergencyTag), false);
                }
                else
                {
                    criticalBatteryCapacityDetected = false;
                    var timerblocks = new List<IMyTimerBlock>();
                    GridTerminalSystem.GetBlocksOfType(timerblocks, tb => MyIni.HasSection(tb.CustomData, TriggerOnNormalCurrentReestablishedTag));
                    timerblocks.ForEach(tb => tb.Trigger());

                    // enable blocks with DisableOnEmergencyTag
                    EnableBlocks(blk => MyIni.HasSection(blk.CustomData, DisableOnEmergencyTag));
                }

                log.Append(string.Format("Battery capacity: {0}%", Math.Round(remainingCapacity * 100, 0)));
            }
            return true;
        }

        bool SubProcessActivateEmergencyPower(StringWrapper log)
        {
            var generators = new List<IMyPowerProducer>();
            GridTerminalSystem.GetBlocksOfType(generators, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, EmergencyPowerTag));

            if (criticalBatteryCapacityDetected)
            {
                log.Append("Emergency power on");
                generators.ForEach(blk => blk.Enabled = true);
            }
            else
            {
                generators.ForEach(blk => blk.Enabled = false);
            }
            return true;
        }

        bool SubProcessDoSomeOtherCheck(StringWrapper log)
        {
            log.Append("other check...");
            log.Append("other check 222...");
            return true;
        }

        bool SubProcessEnableBroadcasting()
        {
            var antenna = FindFirstBlockOfType<IMyRadioAntenna>(blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            antenna.Enabled = true;
            antenna.EnableBroadcasting = true;

            return true;
        }

        bool SubProcessDisableBroadcasting()
        {
            var antenna = FindFirstBlockOfType<IMyRadioAntenna>(blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
            antenna.EnableBroadcasting = false;

            return true;
        }

        bool SubProcessSendBroadcastMessage()
        {
            var message = MyTuple.Create
            (
                Me.CubeGrid.EntityId,
                Me.CubeGrid.CustomName,
                lastShipPosition,
                informationTerminals.Text
            );

            IGC.SendBroadcastMessage(StateBroadcastTag, message);

            return true;
        }
    }
}
