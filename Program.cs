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
        const string ScriptPrefixTag = "SHUTTLE";
        /// <summary>
        /// Safe distance from dock before going to next waypoint
        /// </summary>
        const int SafeDistanceFromDock = 20;
        /// <summary>
        /// The maximum battery capacity to reach during recharging
        /// </summary>
        const float MaxBatteryCapacity = 0.95f;
        /// <summary>
        /// The minimum battery capacity to operate the ship.
        /// If the capacity go down this level the batteries will start recharging if the ship is docked
        /// </summary>
        const float MinBatteryCapacity = 0.5f;
        /// <summary>
        /// If the batteries go below this threshold something is wrong and action should be taken
        /// Timer blocks with the right tag  will be notified and blocks managed by the script will be shutted down if possible.
        /// </summary>
        const float CriticalBatteryCapacity = 0.2f;
        /// <summary>
        /// How long the ship will remain at the waypoint
        /// </summary>
        TimeSpan parkingPeriodAtWaypoint = new TimeSpan(0, 0, 0, 5, 0);
        /// <summary>
        /// whether to use real time (second between calls) or pure UpdateFrequency
        /// for update frequency
        /// </summary>
        readonly bool USE_REAL_TIME = false;
        /// <summary>
        /// Defines the FREQUENCY.
        /// </summary>
        const UpdateFrequency FREQUENCY = UpdateFrequency.Update100;
        /// <summary>
        /// How often the script should update in milliseconds
        /// </summary>
        const int UPDATE_REAL_TIME = 1000;
        /// <summary>
        /// The maximum run time of the script per call.
        /// Measured in milliseconds.
        /// </summary>
        const double MAX_RUN_TIME = 35;
        /// <summary>
        /// The maximum percent load that this script will allow
        /// regardless of how long it has been executing.
        /// </summary> 
        const double MAX_LOAD = 0.8;
        /// <summary>
        /// A wrapper for the <see cref="Echo"/> function that adds the log to the stored log.
        /// This allows the log to be remembered and re-outputted without extra work.
        /// </summary>
        public Action<string> EchoR;

        #region Script state & storage

        /// <summary>
        /// Handle Custom Data settings
        /// </summary>
        public MyIni _ini = new MyIni();
        /// <summary>
        /// Handle script arguments
        /// </summary>
        MyCommandLine _commandLine = new MyCommandLine();
        /// <summary>
        /// A list of commands available to execute using script argument
        /// </summary>
        Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// The time we started the last cycle at.
        /// If <see cref="USE_REAL_TIME"/> is <c>true</c>, then it is also used to track
        /// when the script should next update
        /// </summary>
        DateTime currentCycleStartTime;
        /// <summary>
        /// The time the previous step ended
        /// </summary>
        DateTime previousStepEndTime;
        /// <summary>
        /// The time to wait before starting the next cycle.
        /// Only used if <see cref="USE_REAL_TIME"/> is <c>true</c>.
        /// </summary>
        TimeSpan cycleUpdateWaitTime = new TimeSpan(0, 0, 0, 0, UPDATE_REAL_TIME);
        /// <summary>
        /// The total number of calls this script has had since compilation.
        /// </summary>
        long totalCallCount;
        /// <summary>
        /// The text to echo at the start of each call.
        /// </summary>
        string scriptUpdateText;
        /// <summary>
        /// The current step in the TIM process cycle.
        /// </summary>
        int processStep;
        /// <summary>
        /// All of the process steps that TIM will need to take,
        /// </summary>
        readonly Action[] processSteps;
        /// <summary>
        /// Stores the output of Echo so we can effectively ignore some calls
        /// without overwriting it.
        /// </summary>
        public StringBuilder echoOutput = new StringBuilder();
        /// <summary>
        /// It indicates that batteries need charging
        /// </summary>
        bool lowBatteryCapacityDetected = false;
        /// <summary>
        /// It indicates that batteries are really low capacity
        /// </summary>
        bool criticalBatteryCapacityDetected = false;

        /// <summary>
        /// The current waypoint
        /// </summary>
        Waypoint currentWaypoint;
        /// <summary>
        /// The list of waypoints
        /// </summary>
        List<Waypoint> waypoints = new List<Waypoint>();
        /// <summary>
        /// The remote control block to use for the operations
        /// </summary>
        IMyRemoteControl _remoteControl;
        /// <summary>
        /// The connector to use for docking
        /// </summary>
        IMyShipConnector _dockingConnector;

        IMySensorBlock _sensor;

        IMyTerminalBlock _referenceBlock;

        /// <summary>
        /// The ship position recorded after each step
        /// </summary>
        Vector3D lastShipPosition = Vector3D.Zero;

        /// <summary>
        /// Defines the terminalCycle.
        /// </summary>
        IEnumerator<bool> terminalCycle;

        DisplayTerminal displayTerminals;

        IEnumerator<bool> subProcessStepCycle;

        #endregion

        #region Properties

        /// <summary>
        /// The length of time we have been executing for.
        /// Measured in milliseconds.
        /// </summary>
        int ExecutionTime
        {
            get { return (int)((DateTime.Now - currentCycleStartTime).TotalMilliseconds + 0.5); }
        }

        /// <summary>
        /// The current percent load of the call.
        /// </summary>
        double ExecutionLoad
        {
            get { return Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount; }
        }

        IMyRemoteControl RemoteControl
        {
            get
            {
                if (IsCorrupt(_remoteControl))
                {
                    List<IMyRemoteControl> blocks = new List<IMyRemoteControl>();
                    GridTerminalSystem.GetBlocksOfType(blocks, CollectSameConstruct);
                    _remoteControl = blocks.Find(block => block.IsFunctional & block.IsWorking);
                }
                
                if (_remoteControl == null)
                {
                    EchoR("No working remote control found on the ship.");
                    throw new PutOffExecutionException();
                }
                return _remoteControl;
            }
        }

        IMyShipConnector DockingConnector
        {
            get
            {
                if (IsCorrupt(_dockingConnector))
                {
                    List<IMyShipConnector> blocks = new List<IMyShipConnector>();
                    GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, ScriptPrefixTag));
                    _dockingConnector = blocks.Find(block => block.IsFunctional & block.IsWorking);
                }

                if (_dockingConnector == null)
                {
                    EchoR("No working connector found on the ship.");
                    throw new PutOffExecutionException();
                }
                return _dockingConnector;
            }
        }

        public IMySensorBlock Sensor
        {
            get
            {
                if (IsCorrupt(_sensor))
                {
                    var sensors = new List<IMySensorBlock>();
                    GridTerminalSystem.GetBlocksOfType<IMySensorBlock>(sensors, block => MyIni.HasSection(block.CustomData, ScriptPrefixTag));
                    _sensor = sensors.Find(block => block.IsFunctional);
                }

                if (_sensor == null)
                {
                    EchoR("No sensor found on the ship.");
                }
                return _sensor;
            }
        }

        public IMyTerminalBlock ReferenceBlock
        {
            get
            {
                if (IsCorrupt(_referenceBlock))
                {
                    var blocks = new List<IMyTerminalBlock>();
                    GridTerminalSystem.GetBlocksOfType(blocks, block => MyIni.HasSection(block.CustomData, ScriptPrefixTag + ":reference"));
                    _referenceBlock = blocks.Find(blk => true);
                }

                if (_referenceBlock == null)
                {
                    _referenceBlock = RemoteControl;
                }
                return _referenceBlock;
            }
        }

        #endregion

        #region Version

        const string SCRIPT_NAME = "ED's Automated Shuttle";
        // current script version
        const int VERSION_MAJOR = 1, VERSION_MINOR = 0, VERSION_REVISION = 1;
        /// <summary>
        /// Current script update time.
        /// </summary>
        const string VERSION_UPDATE = "2020-07-30";
        /// <summary>
        /// A formatted string of the script version.
        /// </summary>
        readonly string VERSION_NICE_TEXT = string.Format("v{0}.{1}.{2} ({3})", VERSION_MAJOR, VERSION_MINOR, VERSION_REVISION, VERSION_UPDATE);

        #endregion

        #region Format Strings

        /// <summary>
        /// The format for the text to echo at the start of each call.
        /// </summary>
        const string FORMAT_UPDATE_TEXT = "{0}\n{1}\nLast run: #{{0}} at {{1}}";

        #endregion

        public Program()
        {
            // init echo wrapper
            EchoR = log =>
            {
                echoOutput.AppendLine(log);
                Echo(log);
            };

            _commands["shutdown"] = Shutdown;
            _commands["reset"] = Reset;
            _commands["test"] = Test;
            _commands["start"] = Start;
            _commands["stop"] = Stop;
            _commands["step"] = ExecuteStep;

            RetrieveCustomSetting();
            RetrieveStorage();
            UpdateLastShipPosition();

            displayTerminals = new DisplayTerminal(this);
            terminalCycle = SetTerminalCycle();
            subProcessStepCycle = SetSubProcessStepsCycle();

            // initialise the process steps we will need to do
            processSteps = new Action[]
            {
                ProcessStepResetControl,                // 0
                ProcessStepRechargeBatteries,           // 1
                ProcessStepFindNextWaypoint,            // 2
                ProcessStepDoBeforeUndocking,           // 3
                ProcessStepUndockShip,                  // 4
                ProcessStepMoveAwayFromDock,            // 5  // block during docking (connector connected)
                ProcessStepResetThrustOverride,         // 6
                ProcessStepGoToWaypoint,                // 7
                ProcessStepDisableBroadcasting,         // 8
                ProcessStepTravelToWaypoint,            // 9
                ProcessStepEnableBroadcasting,          // 10
                ProcessStepDockToStation,               // 11
                ProcessStepWaitDockingCompletion,       // 12
                ProcessStepResetThrustOverride,         // 13
                ProcessStepDoAfterDocking,              // 14
                ProcessStepWaitAtWaypoint,              // 15
            };

            Runtime.UpdateFrequency = UpdateFrequency.None;
            //Runtime.UpdateFrequency = FREQUENCY;

            EchoR(string.Format("Compiled {0} {1}", SCRIPT_NAME, VERSION_NICE_TEXT));

            // format terminal info text
            scriptUpdateText = string.Format(FORMAT_UPDATE_TEXT, SCRIPT_NAME, VERSION_NICE_TEXT);
        }

        public void Save()
        {
            Storage = currentWaypoint?.Name;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (USE_REAL_TIME)
            {
                DateTime n = DateTime.Now;
                if (n - currentCycleStartTime >= cycleUpdateWaitTime)
                    currentCycleStartTime = n;
                else
                {
                    Echo(echoOutput.ToString()); // ensure that output is not lost
                    return;
                }
            }
            else
            {
                currentCycleStartTime = DateTime.Now;
            }

            echoOutput.Clear();

            // output terminal info
            EchoR(string.Format(scriptUpdateText, ++totalCallCount, currentCycleStartTime.ToString("h:mm:ss tt")));

            bool commandInvoked = false;
            if (_commandLine.TryParse(argument))
            {
                Action commandAction;

                // Retrieve the first argument. Switches are ignored.
                string command = _commandLine.Argument(0);

                // Now we must validate that the first argument is actually specified, 
                // then attempt to find the matching command delegate.
                if (command == null)
                {
                    Echo("No command specified");
                }
                else if (_commands.TryGetValue(command, out commandAction))
                {
                    // We have found a command. Invoke it.
                    commandAction();
                    commandInvoked = true;
                }
                else
                {
                    Echo($"Unknown command {command}");
                }
            }

            if (processStep == processSteps.Length)
            {
                processStep = 0;
            }
            int processStepTmp = processStep;
            bool didAtLeastOneProcess = false;

            try
            {
                if (!commandInvoked) processSteps[processStep]();
                didAtLeastOneProcess = true;
            }
            catch (PutOffExecutionException) { }
            catch (Exception ex)
            {
                // if the process step threw an exception, make sure we print the info
                // we need to debug it
                string err = "An error occured,\n" +
                    "please give the following information to the developer:\n" +
                    string.Format("Current step on error: {0}\n{1}", processStep, ex.ToString().Replace("\r", ""));
                EchoR(err);
                throw ex;
            }

            // we save last ship position and previous step completed time after every step
            if (processStep != processStepTmp)
            {
                UpdateLastShipPosition();
                previousStepEndTime = DateTime.Now;
                subProcessStepCycle = SetSubProcessStepsCycle();
            }

            EchoR(string.Format("Registered waypoints: #{0}", waypoints.Count()));
            EchoR(string.Format("Destination: {0}", currentWaypoint?.Name ?? "NA"));

            string stepText;
            int theoryProcessStep = processStep == 0 ? processSteps.Count() : processStep;
            int exTime = ExecutionTime;
            double exLoad = Math.Round(100.0f * ExecutionLoad, 1);
            if (processStep == 0 && processStepTmp == 0 && didAtLeastOneProcess)
                stepText = "all steps";
            else if (processStep == processStepTmp)
                stepText = string.Format("step {0} partially", processStep);
            else if (theoryProcessStep - processStepTmp == 1)
                stepText = string.Format("step {0}", processStepTmp);
            else
                stepText = string.Format("steps {0} to {1}", processStepTmp, theoryProcessStep - 1);
            EchoR(string.Format("Completed {0} in {1}ms\n{2}% load ({3} instructions)",
                stepText, exTime, exLoad, Runtime.CurrentInstructionCount));

            if (!terminalCycle.MoveNext())
            {
                terminalCycle.Dispose();
            }
        }
        
    }
}
