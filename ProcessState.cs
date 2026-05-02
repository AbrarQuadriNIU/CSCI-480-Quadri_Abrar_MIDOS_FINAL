namespace MID
{

    //What this does is define states that a process can be in 
    public enum ProcessState
    {
        New,            // Initial state upon creation
        Ready,          // Ready to run, waiting for the OS to deploy it
        Running,        // Currently executing on the CPU
        WaitingAsleep,  // Paused after a SLEEP instruction; SleepCounter ticks down
        WaitingLock,    // Blocked, waiting for a mutex/lock
        WaitingEvent,   // Blocked, waiting for an event signal
        Terminated      // Mission complete, waiting for cleanup
    }
}