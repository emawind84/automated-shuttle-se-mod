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
        /// Defines the FREQUENCY.
        /// </summary>
        private const UpdateFrequency FREQUENCY = UpdateFrequency.Update10;

        // How often the script should update in milliseconds
        const int UPDATE_REAL_TIME = 1000;
        // The maximum run time of the script per call.
        // Measured in milliseconds.
        const double MAX_RUN_TIME = 35;

        // The maximum percent load that this script will allow
        // regardless of how long it has been executing.
        const double MAX_LOAD = 0.8;

        private MyIni _ini = new MyIni();

        /// <summary>
        /// A wrapper for the <see cref="Echo"/> function that adds the log to the stored log.
        /// This allows the log to be remembered and re-outputted without extra work.
        /// </summary>
        public Action<string> EchoR;

        #region Script state & storage

        /// The time we started the last cycle at.
        /// If <see cref="USE_REAL_TIME"/> is <c>true</c>, then it is also used to track
        /// when the script should next update
        /// </summary>
        DateTime currentCycleStartTime;

        DateTime currentWaitTimeAtStation;
        TimeSpan maxWaitTimeAtStation = new TimeSpan(0, 0, 0, 5, 0);

        /// <summary>
        /// The time to wait before starting the next cycle.
        /// Only used if <see cref="USE_REAL_TIME"/> is <c>true</c>.
        /// </summary>
        TimeSpan cycleUpdateWaitTime = new TimeSpan(0, 0, 0, 0, UPDATE_REAL_TIME);
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
        StringBuilder echoOutput = new StringBuilder();

        bool firstTimeStepExecution = true;

        Waypoint currentWaypoint;
        List<Waypoint> waypoints = new List<Waypoint>();

        IMyRemoteControl remoteControl;

        Vector3D lastDockedPosition = Vector3D.Zero;

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
        const string FORMAT_TIM_UPDATE_TEXT = "Automated Shuttle\n{0}\nLast run: #{{0}} at {{1}}";

        #endregion

        public Program()
        {
            // init echo wrapper
            EchoR = log =>
            {
                echoOutput.AppendLine(log);
                Echo(log);
            };

            RetrieveCustomData();
            RetrieveStorage();

            // initialise the process steps we will need to do
            processSteps = new Action[]
            {
                FindNextWaypoint,
                GoToWaypoint,
                TravelToWaypoint,
                DockToStation,
                WaitDocking,
                DoAfterDocking,
                RechargeBatteries,
                WaitAtStation,
                DoBeforeUndocking,
                UndockShip,
                MoveAwayFromDock
            };

            Runtime.UpdateFrequency = FREQUENCY;

            EchoR("Compiled Automated Shuttle " + VERSION_NICE_TEXT);

            // format terminal info text
            timUpdateText = string.Format(FORMAT_TIM_UPDATE_TEXT, VERSION_NICE_TEXT);
        }

        public void Save()
        {
            Storage = currentWaypoint.Name;
        }

        private void RetrieveStorage()
        {
            var currentWaypointName = Storage;
            if (currentWaypointName != null)
            {
                currentWaypoint = waypoints.Find(waypoint => waypoint.Name == currentWaypointName);
            }
        }

        private void RetrieveCustomData()
        {
            // init settings
            _ini.TryParse(Me.CustomData);

            string customDataWaypoints = _ini.Get("SHUTTLE", "Waypoints").ToString() ?? "";
            string[] _waypoints = customDataWaypoints.Split(',');
            for (int i = 0; i < _waypoints.Count(); i++)
            {
                waypoints.Add(new Waypoint(_waypoints[i]));
            }
        }

        private IMyRemoteControl RemoteControl {
            get {
                List<IMyRemoteControl> blocks = new List<IMyRemoteControl>();
                GridTerminalSystem.GetBlocksOfType(blocks);
                IMyRemoteControl controlBlock = blocks.Find(block => block.IsFunctional & block.IsWorking);
                if (controlBlock == null)
                {
                    EchoR("No working remote control found on the ship.");
                    throw new PutOffExecutionException();
                }
                remoteControl = controlBlock;
                return remoteControl;
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            currentCycleStartTime = DateTime.Now;
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
                EchoR(string.Format("Registered waypoints: #{0}", waypoints.Count()));
                EchoR(string.Format("Next waypoint: {0}", currentWaypoint.Name));
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
            EchoR(string.Format("Completed {0} in {1}ms, {2}% load ({3} instructions)",
                stepText, exTime, exLoad, Runtime.CurrentInstructionCount));
        }

        #region Process Steps

        // step 0
        public void FindNextWaypoint()
        {
            if (currentWaypoint == null)
            {
                currentWaypoint = waypoints.First();
            }
            else
            {
                double distanceFromWaypoint = Vector3D.Distance(currentWaypoint.Coords, Me.GetPosition());
                
                EchoR("distanceFromWaypoint: " + distanceFromWaypoint);
                if (distanceFromWaypoint < 100)
                {
                    EchoR("changing waypoint");
                    int totalWaypoints = waypoints.Count();
                    int index = waypoints.FindIndex(wp => wp.Name == currentWaypoint.Name);
                    currentWaypoint = waypoints[(index + 1) % totalWaypoints];
                }
                
            }
            
            processStep++;
        }

        // step 1
        public void GoToWaypoint() {
            skipIfDocked();

            IMyRemoteControl controlBlock = RemoteControl;
            controlBlock.SetCollisionAvoidance(true);
            controlBlock.FlightMode = FlightMode.OneWay;
            controlBlock.SetDockingMode(false);
            if (currentWaypoint.HasDock) controlBlock.SetDockingMode(true);

            Vector3 controlBlockPosition = controlBlock.GetPosition();
            float distanceToWaypoint = Vector3.Distance(controlBlockPosition, currentWaypoint.Coords);
            if (distanceToWaypoint > 50) {
                controlBlock.ClearWaypoints();
                controlBlock.AddWaypoint(currentWaypoint.Coords, currentWaypoint.Name);
                controlBlock.SetAutoPilotEnabled(true);
            }
            
            processStep++;
        }

        // step 2
        public void TravelToWaypoint() {
            if (RemoteControl.GetShipSpeed() == 0) {
                processStep++;
            }
        }

        // step 3
        public void DockToStation()
        {
            skipIfDocked();
            skipIfWaypointWithoutDock();

            // start docking
            List<IMyProgrammableBlock> blocks = new List<IMyProgrammableBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => blk.CustomName.Contains("[DOCKING]"));
            IMyProgrammableBlock programmableBlock = blocks.Find(block => block.IsFunctional & block.IsWorking);

            if (programmableBlock.IsRunning) { return; }
            programmableBlock.TryRun(currentWaypoint.Name);
            
            processStep++;
        }

        // step 4
        public void WaitDocking() {
            skipIfDocked();
            skipIfWaypointWithoutDock();
            
            List<IMyShipConnector> blocks = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => blk.CustomName.Contains("[CONNECTOR]"));
            IMyShipConnector connector = blocks.First();
            if (connector.Status == MyShipConnectorStatus.Connectable || connector.Status == MyShipConnectorStatus.Connected) {
                processStep++;
            }
        }

        // step 5
        public void DoAfterDocking()
        {
            lastDockedPosition = RemoteControl.GetPosition();
            var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => door.CustomName.Contains("[DOOR]"));
            doors.ForEach(door => {
                //door.OpenDoor();
                door.Enabled = true;
            });
            processStep++;
        }

        // step 6
        public void RechargeBatteries()
        {
            skipIfUndocked();
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, battery => battery.IsWorking);

            if (RemainingBatteryCapacity(batteries) < 0.5)
            {
                batteries.ForEach(battery => {
                    battery.ChargeMode = ChargeMode.Auto;
                });
            }
            else
            {
                processStep++;
            }

        }

        // step 7
        public void WaitAtStation()
        {
            skipIfUndocked();

            DateTime n = DateTime.Now;
            if (firstTimeStepExecution) {
                currentWaitTimeAtStation = n;
                firstTimeStepExecution = false;
            }
            if (n - currentWaitTimeAtStation >= maxWaitTimeAtStation)
            {
                firstTimeStepExecution = true;
                processStep++;
            }
        }

        // step 7
        public void DoBeforeUndocking()
        {
            var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => door.CustomName.Contains("[DOOR]"));
            doors.ForEach(door => {
                door.CloseDoor();
                door.Enabled = false;
            });
            processStep++;
        }

        // step 8
        public void UndockShip()
        {
            skipIfWaypointWithoutDock();

            List<IMyShipConnector> blocks = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(blocks, blk => blk.CustomName.Contains("[CONNECTOR]"));
            IMyShipConnector connector = blocks.First();
            connector.Disconnect();
            connector.PullStrength = 0;
            //connector.Orientation
            processStep++;
        }

        // step 9
        public void MoveAwayFromDock()
        {
            skipIfWaypointWithoutDock();

            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors, blk => blk.CustomName.Contains("[CONNECTOR]"));
            IMyShipConnector connector = connectors.First();
            MyBlockOrientation connectorOrientation = connector.Orientation;
            var thrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(thrusters);
            var connectorThrusters = new List<IMyThrust>();
            foreach (IMyThrust thrust in thrusters)
            {
                if (thrust.Orientation.Forward == connectorOrientation.Forward)
                {
                    connectorThrusters.Add(thrust);
                }
            }
            
            double currentSpeed = RemoteControl.GetShipSpeed();
            double distanceFromDock = Vector3D.Distance(lastDockedPosition, Me.GetPosition());
            if (distanceFromDock < 50 && currentSpeed < 5)
            {
                connectorThrusters.ForEach(thrust =>
                {
                    thrust.ThrustOverridePercentage += 0.1f;
                });
            }
            else if (distanceFromDock > 50)
            {
                connectorThrusters.ForEach(thrust => {
                    thrust.ThrustOverridePercentage = 0;
                });
                processStep++;
            }
            
        }

        public void skipIfDocked()
        {
            // check if the ship is connected to a grid
            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors, blk => blk.CustomName.Contains("[CONNECTOR]"));
            IMyShipConnector connector = connectors.First();
            if (connector.Status == MyShipConnectorStatus.Connected || connector.Status == MyShipConnectorStatus.Connectable)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        public void skipIfUndocked()
        {
            // check if the ship is connected to a grid
            List<IMyShipConnector> connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors, blk => blk.CustomName.Contains("[CONNECTOR]"));
            IMyShipConnector connector = connectors.First();
            if (connector.Status == MyShipConnectorStatus.Unconnected)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        public void skipIfWaypointWithoutDock()
        {
            if (!currentWaypoint.HasDock)
            {
                processStep++;
                throw new PutOffExecutionException();
            }
        }

        private float RemainingBatteryCapacity(List<IMyBatteryBlock> batteries)
        {
            float totalCurrentCapacity = 0; float totalMaxCapacity = 0;
            for (int i = 0; i < batteries.Count(); i++)
            {
                float capacity = batteries[i].CurrentStoredPower / batteries[i].MaxStoredPower;
                if (batteries[i].IsCharging && capacity < 0.8)
                {
                    totalCurrentCapacity = 0;
                }
                else
                {
                    totalCurrentCapacity = batteries[i].CurrentStoredPower;
                }
                totalMaxCapacity = batteries[i].MaxStoredPower;
            }

            return totalCurrentCapacity / totalMaxCapacity;
        }

        #endregion

        private class Waypoint {

            public string Name { get; }

            public Vector3D Coords { get; }

            public bool HasDock { get; }

            public Waypoint(string name, Vector3D coords, bool hasDock)
            {
                this.Coords = coords;
                this.HasDock = hasDock;
                this.Name = name;
            }

            public Waypoint(string waypointData)
            {
                //GPS: WAYPOINT 1:227004.83:227029.62:227020.38:
                MyWaypointInfo waypointInfo;
                if (MyWaypointInfo.TryParse(waypointData, out waypointInfo))
                {
                    this.Name = waypointInfo.Name;
                    this.Coords = waypointInfo.Coords;
                    this.HasDock = true;
                }
            }
        }

        /// <summary>
        /// Checks if the current call has exceeded the maximum execution limit.
        /// If it has, then it will raise a <see cref="PutOffExecutionException:T"/>.
        /// </summary>
        /// <returns>True.</returns>
        /// <remarks>This methods returns true by default to allow use in the while check.</remarks>
        bool DoExecutionLimitCheck()
        {
            if (ExecutionTime > MAX_RUN_TIME || ExecutionLoad > MAX_LOAD)
                throw new PutOffExecutionException();
            return true;
        }

        /// <summary>
        /// Thrown when we detect that we have taken up too much processing time
        /// and need to put off the rest of the exection until the next call.
        /// </summary>
        class PutOffExecutionException : Exception
        {
        }
    }
}
