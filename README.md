# automated-shuttle-se-mod
Automated Shuttle a Space Engineers Mod

The script main purpose was to run on a shuttle going back and forth between two or more waypoints, docking, stop at each waypoint, charging batteries, wait for a certain period for passengers, departing for the next waypoint and so on. A new orbit mode is available as well where you need to set three parameters and the ship will start orbiting around a predefined point in space, it will automatically go to the nearest point along the orbit and it will follow the same orbit until the script is shutted down.

## Script Setup

Place the tag `[SHUTTLE]` in the Custom Data of the Programmable Block where the script need to run, then add the following properties as needed:

- `ParkingPeriod=120` - How long it will stay, in seconds, at the current waypoint before departure (only if the waypoint has the StopAtWaypoint set, `true` by default in non Orbit Mode)
- `ManageBattery=true` - Decide whether or not the script should handle the battery charging when connected to another grid.
- `Waypoints=W1,W2` - Set the waypoints, separated by comma, using the game GPS format (`GPS:Waypoint 2:139680.9:-138123.54:77539.84:`), reached the last waypoint, the ship will set route for the first (1 -> 2 -> 3 -> 1 -> 2 -> 3).

    Waypoints can also be written like below:

    ```
    Waypoints=
    |GPS:Waypoint 1:52693.37:-103844.05:40101.32:,
    |GPS:Waypoint 2:139680.9:-138123.54:77539.84:
    ```

There are other settings that can be changed in the script as well:

`SafeDistanceFromDock` - Safe distance from the dock point before activating the autopilot for the next waypoint, the ship will slowly move away from the dock until it reach the set distance, `20 meters` by default.

`MinBatteryCapacity` - The minimum battery capacity to operate the ship. If the capacity go down this level the batteries will start recharging if the ship is docked.

## Blocks Setup

First of all the tag that the script use is `SHUTTLE`, and it can be changed directly in the script, all the others tags used are the composition of the main tag, plus the specific one.

A Remote Control block is the only requirement to use the script.

A Connector is required for docking if you have the script, to register a connector, the script tag have to be placed in the Custom Data area of the block. The default tag is `[SHUTTLE]`.

Sensors are necessary to enable detection of grids around, this will prevent the ship from crashing during the undocking and for other circumstances. No settings is required just put the blocks and they will be used.

A reference block is used as center position for the grid and also for deciding the direction during some manouvers. The tag `[SHUTTLE:ReferenceBlock]` have to be placed in the Custom Data of the block to register it as reference block. I recommend to set the Connector as reference block, in case the docking procedure is enabled.

Debug terminal blocks are used for debugging, the tag `[SHUTTLE:DebugTerminal]` have to be placed in the Custom Data of a display block or control seat. You can decide where to display the log by adding the property `display=#` just below the tag, where # is the display number.

Info Display terminal are used to display basic information to the player, docking, undocking, traveling time, departure, etc.
Add the tag `[SHUTTLE:DisplayTermina]` to the display block, then add below the property `display=#` to use another display in a multi display block.

Docking script can be registered adding the tag `[SHUTTLE:DockingScript]` to a Programmable Block's Custom Data that has the script for docking, it will be executed during the docking phase.

Emergency Power block can be used if you want a backup power in case batteries get too low, this will prevent the ship from shutting down, place the tag `[SHUTTLE:EmergencyPower]` in the Custom Data area of a power generator block like Reactor or Hydrogen Engine. The generator has to be switched off manually the first time, then it will be activated automatically in case of emergency.

There are also other tag that can be used that will start or toggle specific blocks at some point, during docking, undocking, critical current, etc.

- `[SHUTTLE:TriggerBeforeUndocking]` - Trigger the timer block before undocking

- `[SHUTTLE:TriggerAfterDocking]` - Trigger the timer block at the end of docking

- `[SHUTTLE:TriggerOnCriticalCurrentDetected]` - Trigger the timer block that has this tag if critical current is detected

- `[SHUTTLE:TriggerOnNormalCurrentReestablished]` - Trigger the timer block that has this tag if the current come back to normal level

- `[SHUTTLE:DisableOnEmergency]` - Disable the block that has this tag in case of critical current

## Orbit Mode

The orbit mode is the one that I enjoy most, easy to set and just cool to see it works.

These are the properties that need to be set in the Custom Data of the Programmable Block where the script is running:

- `OrbitMode=true` - Enable orbit mode
- `OrbitCenterPosition=GPS:Center Point:139680.9:-138123.54:77539.84:` - The center position using the game GPS format (remove the last color information from the GPS if it doesn't work)
- `OrbitRadius=100000` - How far from the center point the ship will orbit in meters


## How to Run

The script will not start automatically, this is to prevent issues and give the player time to set everything up, there are several commands available that need to be passed as argument to the script.

- `start` - Start the script
- `stop` - Pause the script, it can be resumed with the `start` command
- `reset` - Restart the script, skip all the remaining steps
- `shutdown` - Shutdown the script, reset several block's conditions
- `step #` - Skip to the desired step (for debugging)
- `nextstep` - Go to the next step, no matter what.
- `addwaypoint` - Add a new waypoint, it will not be saved in Custom Data
- `nextwaypoint` - Go to the next waypoint, skip all the remaining steps

## GitHub

https://github.com/emawind84/automated-shuttle-se-mod


Supplemental script for listening to broadcast messages

https://github.com/emawind84/automated-shuttle-listener-se-mod