using System;
using System.Collections.Generic;

//PROCESS CONTROL BLOCK
//What it does is a data structure that contains all the information about a process that the OS needs to manage it.
//aka a dictionary
namespace MID
{
    public class PCB
    {
        // Identification 
        public int ProcessId { get; set; }
        public ProcessState State { get; set; }

        // CPU State Snapshot 
        public int[] Registers { get; set; } = new int[15];
        public bool SignFlag { get; set; }
        public bool ZeroFlag { get; set; }

        // Scheduling & Metrics
        public int Priority { get; set; }         // 1 through 32
        public int BaselinePriority { get; set; } // Saved priority before any inversion bump
        public int TimeQuantum { get; set; }      // Ticks allowed before context switch
        public int ClockCycles { get; set; }      // Total clock cycles consumed (cumulative)
        public int SleepCounter { get; set; }     // Ticks remaining until wake up
        public int ContextSwitches { get; set; }  // Times swapped in/out
        public int PageFaultCount { get; set; }    

        // Memory Boundaries 
        public int CodeSize { get; set; }
        public int StackSize { get; set; }
        public int DataSize { get; set; }

        public int HeapStart { get; set; }
        public int HeapEnd { get; set; }
        public int ProcessMemorySize { get; set; }

        // Memory Management (Paging) 
        public int[] PageTable { get; set; }
        public List<int> WorkingSetPages { get; set; } = new List<int>();

        // Heap Management 
        public class HeapBlock
        {
            public int LogicalAddress { get; set; }
            public int Size { get; set; }
            public bool IsFree { get; set; }
        }

        public int NextHeapAddress { get; set; }
        public List<HeapBlock> HeapBlocks { get; set; } = new List<HeapBlock>();

        //  Lock Ownership Tracking 
        // Tracks which locks this process currently holds, for cleanup on exit
        public List<int> HeldLocks { get; set; } = new List<int>();

        // Tracks which specific lock this process is currently blocked on.
        // -1 means not blocked on any lock.
        // Used by the Scheduler for precise priority inversion restoration.
        public int WaitingOnLockId { get; set; } = -1;

        // Constructor
        public PCB(int processId, int priority = 16)
        {
            ProcessId = processId;
            State = ProcessState.New;
            Priority = priority;
            BaselinePriority = priority;
            TimeQuantum = 10;
            ClockCycles = 0;
            ContextSwitches = 0;
            PageFaultCount = 0;
        }
    }
}