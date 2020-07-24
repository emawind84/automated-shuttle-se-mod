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

        public MyIni _ini = new MyIni();

        /// <summary>
        /// A wrapper for the <see cref="Echo"/> function that adds the log to the stored log.
        /// This allows the log to be remembered and re-outputted without extra work.
        /// </summary>
        public Action<string> EchoR;

        #region Script state & storage

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
        string timUpdateText;
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

        bool lowBatteryCapacityDetected = false;
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
        IMyRemoteControl remoteControl;
        /// <summary>
        /// The connector to use for docking
        /// </summary>
        IMyShipConnector dockingConnector;

        Vector3D lastDockedPosition = Vector3D.Zero;

        /// <summary>
        /// Defines the terminalCycle.
        /// </summary>
        IEnumerator<bool> terminalCycle;

        DisplayTerminal displayTerminals;

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
                if (IsCorrupt(remoteControl))
                {
                    List<IMyRemoteControl> blocks = new List<IMyRemoteControl>();
                    GridTerminalSystem.GetBlocksOfType(blocks);
                    remoteControl = blocks.Find(block => block.IsFunctional & block.IsWorking);
                }
                
                if (remoteControl == null)
                {
                    EchoR("No working remote control found on the ship.");
                    throw new PutOffExecutionException();
                }
                return remoteControl;
            }
        }

        IMyShipConnector DockingConnector
        {
            get
            {
                if (IsCorrupt(dockingConnector))
                {
                    List<IMyShipConnector> blocks = new List<IMyShipConnector>();
                    GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, "shuttle"));
                    dockingConnector = blocks.Find(block => block.IsFunctional & block.IsWorking);
                }

                if (dockingConnector == null)
                {
                    EchoR("No working connector found on the ship.");
                    throw new PutOffExecutionException();
                }
                return dockingConnector;
            }
        }
        
        #endregion

        #region Version

        // current script version
        const int VERSION_MAJOR = 1, VERSION_MINOR = 0, VERSION_REVISION = 1;
        /// <summary>
        /// Current script update time.
        /// </summary>
        const string VERSION_UPDATE = "2020-07-20";
        /// <summary>
        /// A formatted string of the script version.
        /// </summary>
        readonly string VERSION_NICE_TEXT = string.Format("v{0}.{1}.{2} ({3})", VERSION_MAJOR, VERSION_MINOR, VERSION_REVISION, VERSION_UPDATE);

        #endregion

        #region Format Strings

        /// <summary>
        /// The format for the text to echo at the start of each call.
        /// </summary>
        const string FORMAT_UPDATE_TEXT = "Automated Shuttle\n{0}\nLast run: #{{0}} at {{1}}";

        #endregion

        public Program()
        {
            // init echo wrapper
            EchoR = log =>
            {
                echoOutput.AppendLine(log);
                Echo(log);
            };

            RetrieveCustomSetting();
            RetrieveStorage();

            displayTerminals = new DisplayTerminal(this);
            terminalCycle = SetTerminalCycle();

            // initialise the process steps we will need to do
            processSteps = new Action[]
            {
                ResetControl,            // 0
                FindNextWaypoint,        // 1
                DoBeforeUndocking,       // 2
                UndockShip,              // 3
                MoveAwayFromDock,        // 4
                ResetOverrideThruster,   // 5
                GoToWaypoint,            // 6
                TravelToWaypoint,        // 7
                DockToStation,           // 8
                WaitDockingCompletion,   // 9
                DoAfterDocking,          // 10
                RechargeBatteries,       // 11
                WaitAtWaypoint,          // 12
            };

            Runtime.UpdateFrequency = FREQUENCY;

            EchoR("Compiled Automated Shuttle " + VERSION_NICE_TEXT);

            // format terminal info text
            timUpdateText = string.Format(FORMAT_UPDATE_TEXT, VERSION_NICE_TEXT);
        }

        public void Save()
        {
            Storage = currentWaypoint.Name;
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
            if (processStep == processSteps.Count())
            {
                processStep = 0;
            }
            int processStepTmp = processStep;

            // output terminal info
            EchoR(string.Format(timUpdateText, ++totalCallCount, currentCycleStartTime.ToString("h:mm:ss tt")));

            try
            {
                processSteps[processStep]();
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

            if (processStep != processStepTmp)
            {
                // the step ended we set the step end date
                previousStepEndTime = DateTime.Now;
            }

            EchoR(string.Format("Registered waypoints: #{0}", waypoints.Count()));
            EchoR(string.Format("Next waypoint: {0}", currentWaypoint?.Name ?? "NA"));

            string stepText;
            int theoryProcessStep = processStep == 0 ? processSteps.Count() : processStep;
            int exTime = ExecutionTime;
            double exLoad = Math.Round(100.0f * ExecutionLoad, 1);
            if (processStep == 0 && processStepTmp == 0)
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
                EchoR(string.Format("Disposed terminalCycle, current idx: {0}", terminalCycle.Current));
            }
        }

        #region Process Steps

        void ResetControl()
        {
            var cockpits = new List<IMyCockpit>();
            GridTerminalSystem.GetBlocksOfType(cockpits);
            cockpits.ForEach(cockpit => {
                cockpit.ControlThrusters = true;
            });

            ResetBatteryMode();
            ZeroThrust();
            
            processStep++;
        }

        void FindNextWaypoint()
        {
            if (waypoints.Count() == 0)
            {
                EchoR("No waypoint defined");
                throw new PutOffExecutionException();
            }
            if (currentWaypoint == null)
            {
                currentWaypoint = waypoints.First();
            }
            else
            {
                double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, Me.GetPosition());
                
                if (distanceFromWaypoint < 100)
                {
                    int totalWaypoints = waypoints.Count();
                    int index = waypoints.FindIndex(wp => wp.Name == currentWaypoint.Name);
                    currentWaypoint = waypoints[(index + 1) % totalWaypoints];
                }
                
            }

            processStep++;
        }

        void DoBeforeUndocking()
        {
            lastDockedPosition = RemoteControl.GetPosition();

            /*
            var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => MyIni.HasSection(door.CustomData, "shuttle"));
            doors.ForEach(door => {
                door.CloseDoor();
                door.Enabled = false;
            });
            */

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, "shuttle:beforeundocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            processStep++;
        }

        void UndockShip()
        {
            DockingConnector.Disconnect();
            DockingConnector.PullStrength = 0;

            if (DockingConnector.Status != MyShipConnectorStatus.Connected)
            {
                processStep++;
            }
        }

        void MoveAwayFromDock()
        {
            SkipIfNoGridNearby();

            var thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrusters, thruster => thruster.Orientation.Forward == DockingConnector.Orientation.Forward);
            double currentSpeed = RemoteControl.GetShipSpeed();
            double distanceFromDock = Vector3D.Distance(lastDockedPosition, Me.GetPosition());

            if (distanceFromDock < SafeDistanceFromDock && currentSpeed < 5)
            {
                thrusters.ForEach(thrust =>
                {
                    thrust.ThrustOverridePercentage += 0.1f;
                });
            }
            else if (distanceFromDock > SafeDistanceFromDock)
            {
                processStep++;
            }

        }

        void ResetOverrideThruster()
        {
            ZeroThrust();

            processStep++;
        }

        void GoToWaypoint() {
            SkipIfDocked();

            IMyRemoteControl controlBlock = RemoteControl;
            controlBlock.SetCollisionAvoidance(true);
            controlBlock.FlightMode = FlightMode.OneWay;
            controlBlock.SetDockingMode(false);
            if (currentWaypoint.WaitAtWaypoint) controlBlock.SetDockingMode(true);

            Vector3 controlBlockPosition = controlBlock.GetPosition();
            float distanceToWaypoint = Vector3.Distance(controlBlockPosition, currentWaypoint.Coords);
            if (distanceToWaypoint > 50) {
                controlBlock.ClearWaypoints();
                controlBlock.AddWaypoint(currentWaypoint.Coords, currentWaypoint.Name);
                controlBlock.SetAutoPilotEnabled(true);
            }

            processStep++;
        }

        void TravelToWaypoint() {
            SkipIfDocked();

            double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, Me.GetPosition());
            if (Math.Round(RemoteControl.GetShipSpeed(), 0) == 0 && distanceFromWaypoint < 100) {
                processStep++;
            }
        }

        void DockToStation()
        {
            SkipIfDocked();

            // start docking
            List<IMyProgrammableBlock> blocks = new List<IMyProgrammableBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => MyIni.HasSection(blk.CustomData, "shuttle:docking"));
            IMyProgrammableBlock programmableBlock = blocks.Find(block => block.IsFunctional & block.IsWorking);
            if (programmableBlock == null) {
                processStep++;
                throw new PutOffExecutionException();
            }
            if (programmableBlock.IsRunning) { return; }
            programmableBlock.TryRun(currentWaypoint.Name);

            processStep++;
        }

        void WaitDockingCompletion() {
            SkipIfNoGridNearby();
            SkipOnTimeout(30);

            if (DockingConnector.Status == MyShipConnectorStatus.Connectable 
                || DockingConnector.Status == MyShipConnectorStatus.Connected) {

                processStep++;
            }
        }

        void DoAfterDocking()
        {
            SkipIfNoGridNearby();
            
            /*var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => MyIni.HasSection(door.CustomData, "shuttle"));
            doors.ForEach(door => {
                //door.OpenDoor();
                door.Enabled = true;
            });*/

            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlocksOfType(timerBlocks, block => MyIni.HasSection(block.CustomData, "shuttle:afterdocking"));
            timerBlocks.ForEach(timerBlock => timerBlock.Trigger());

            processStep++;
        }

        void RechargeBatteries()
        {
            SkipIfUndocked();
            
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, battery => MyIni.HasSection(battery.CustomData, "shuttle"));
            //EchoR(string.Format("Found #{0} batteries", batteries.Count()));
            if (batteries.Count() == 0)
            {
                processStep++;
                throw new PutOffExecutionException();
            }

            float remainingCapacity = RemainingBatteryCapacity(batteries);
            
            if (remainingCapacity < MinBatteryCapacity 
                || (remainingCapacity < MaxBatteryCapacity && lowBatteryCapacityDetected))
            {
                EchoR(string.Format("Charging batteries: {0}%", Math.Round(remainingCapacity * 100, 0)));
                lowBatteryCapacityDetected = true;
                
                batteries.ForEach(battery => {
                    if (!battery.IsCharging)
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                    }
                    else
                    {
                        battery.ChargeMode = ChargeMode.Recharge;
                    }
                });
            }
            else
            {
                lowBatteryCapacityDetected = false;
                batteries.ForEach(battery => {
                    battery.ChargeMode = ChargeMode.Auto;
                });

                processStep++;
            }
        }

        void WaitAtWaypoint()
        {
            SkipIfNoGridNearby();
            DateTime n = DateTime.Now;
            if (n - previousStepEndTime >= parkingPeriodAtWaypoint)
            {
                processStep++;
            }
        }
        

        #endregion

    }
}
