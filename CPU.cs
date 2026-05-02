using System;
using System.Collections.Generic;
using static MID.PCB;

namespace MID
{
    public class CPU
    {
        // REGISTER LAYOUT  (Module 1 spec — 14 registers, 1-indexed)
        //   r1  – r10  : general purpose (programmer-accessible)
        //   r11: Instruction Pointer (IP)  — NOT accessible to programs
        //   r12: Current Process Id
        //   r13: Stack Pointer (SP)
        //   r14: Global Memory Start address
        private int[] registers = new int[15];

        private bool zeroFlag; // When comparison brings 0
        private bool signFlag; // Set when comparison brings negative

        private MemoryManager _memory;


        private const int IP_REGISTER = 11;
        private const int PID_REGISTER = 12;
        private const int SP_REGISTER = 13;
        private const int GLOBAL_REGISTER = 14;

        // Internal CPU state flags 
        private bool _halted = false;
        private bool _blockedByLock = false;
        private bool _blockedByEvent = false;
        private bool _sleeping = false;
        private int _blockedByLockId = -1; // Which specific lock caused the block
        private bool _justSignaledEvent = false;



     
        // MODULE 4
        // _locks [1-10] : 0 = free, else = owning ProcessId
        // _events[1-10] : true = Signaled, false = Non-signaled
        private int[] _locks = new int[11];
        private bool[] _events = new bool[11];

        private int _currentProcessId;

        // Exposed to the Scheduler for priority inversion bookkeeping
        public int[] LockWaiterMaxPriority { get; } = new int[11];
        public bool IsLockFree(int id) => _locks[id] == 0;
        public bool IsLockOwnedBy(int id, int pid) => _locks[id] == pid;
        public int GetLockOwner(int id) => _locks[id];
        public bool IsEventSignaled(int id) => _events[id];

        // Heap context for the currently-running process 
        // CPU manages this
        private List<HeapBlock> _activeHeapBlocks;
        private int _activeNextHeapAddress;
        private int _activeHeapEnd;

        // Reference to the currently running PCB so instructions like SETPRIORITY
        // can apply changes immediately rather than deferring to SaveState
        private PCB _activePcb;

        public CPU(MemoryManager memory)
        {
            _memory = memory;
        }

        // FETCH
        // Read both 4-byte arguments from memory at current IP
      
        private void FetchArgs(out int arg1, out int arg2)
        {
            int ip = registers[IP_REGISTER];
            arg1 = _memory.ReadInt32(ip + 1);
            arg2 = _memory.ReadInt32(ip + 5);
        }

      
        // EXECUTE 
        // Returns the actual number of cycles consumed.
    
        public int RunProcess(int timeQuantum)
        {
            bool running = true;
            int cyclesUsed = 0;

            while (running && cyclesUsed < timeQuantum)
            {
                int ip = registers[IP_REGISTER];

                if (ip < 0 || ip >= _memory.Size)
                    throw new Exception($"[CPU] IP {ip} out of bounds — terminating process.");

                Opcode opCode = (Opcode)_memory.ReadByte(ip);
                FetchArgs(out int arg1, out int arg2);

                bool jumped = false;
                cyclesUsed++;   // Every instruction costs exactly one clock cycle (Module 1)

                switch (opCode)
                {
                 
                    // CONTROL FLOW

                    case Opcode.EXIT:
                        running = false;
                        _halted = true;
                        break;

                    case Opcode.SLEEP:
                        // Sleep rx — sleep the # of cycles in rx.
                        // If rx == 0, the process sleeps indefinitely
                        // IP still advances; the process resumes at the NEXT instruction on wake
                        _sleepDuration = registers[arg1];
                        _sleeping = true;
                        running = false;
                        break;

                   
                    // UNCONDITIONAL JUMPS                 
                    case Opcode.JMP:
                        registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + registers[arg1];
                        jumped = true;
                        break;

                    case Opcode.JMPI:
                        registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + arg1;
                        jumped = true;
                        break;

                    case Opcode.JMPA:
                        registers[IP_REGISTER] = arg1;
                        jumped = true;
                        break;

                    
                    // CONDITIONAL JUMPS — LESS THAN  (sign flag set)

                    case Opcode.JLT:
                        if (signFlag) { registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + registers[arg1]; jumped = true; }
                        break;

                    case Opcode.JLTI:
                        if (signFlag) { registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + arg1; jumped = true; }
                        break;

                    case Opcode.JLTA:
                        if (signFlag) { registers[IP_REGISTER] = arg1; jumped = true; }
                        break;

                    // CONDITIONAL JUMPS — GREATER THAN  (sign clear AND not zero)

                    case Opcode.JGT:
                        if (!signFlag && !zeroFlag) { registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + registers[arg1]; jumped = true; }
                        break;

                    case Opcode.JGTI:
                        if (!signFlag && !zeroFlag) { registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + arg1; jumped = true; }
                        break;

                    case Opcode.JGTA:
                        if (!signFlag && !zeroFlag) { registers[IP_REGISTER] = arg1; jumped = true; }
                        break;


                    // CONDITIONAL JUMPS — EQUAL  (zero flag set)

                    case Opcode.JE:
                        if (zeroFlag) { registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + registers[arg1]; jumped = true; }
                        break;

                    case Opcode.JEI:
                        if (zeroFlag) { registers[IP_REGISTER] = (registers[IP_REGISTER] + 9) + arg1; jumped = true; }
                        break;

                    case Opcode.JEA:
                        if (zeroFlag) { registers[IP_REGISTER] = arg1; jumped = true; }
                        break;


                    // SUBROUTINE CALL / RETURN
                    //


                    case Opcode.CALL:
                        {
                            int retAddr = registers[IP_REGISTER] + 9; // address of instruction after CALL
                            registers[SP_REGISTER] -= 4;
                            _memory.WriteInt32(registers[SP_REGISTER], retAddr);
                            registers[IP_REGISTER] = retAddr + registers[arg1]; // relative from next instruction
                            jumped = true;
                            break;
                        }

                    case Opcode.CALLM:
                        {
                            int retAddr = registers[IP_REGISTER] + 9;
                            int offset = _memory.ReadInt32(registers[arg1]); // [rx]
                            registers[SP_REGISTER] -= 4;
                            _memory.WriteInt32(registers[SP_REGISTER], retAddr);
                            registers[IP_REGISTER] = retAddr + offset; // relative from next instruction
                            jumped = true;
                            break;
                        }

                    case Opcode.RET:
                        {
                            int retAddr = _memory.ReadInt32(registers[SP_REGISTER]);
                            registers[SP_REGISTER] += 4;
                            registers[IP_REGISTER] = retAddr;
                            jumped = true;
                            break;
                        }

         
                    // DATA MOVEMENT
                    // Refrence Logic
                    //   Movi  r2, #4   rx ← n
                    //   Movr  r2, r3   rx ← ry
                    //   Movmr r2, r3   rx ← [ry]
                    //   Movrm r3, r2  [rx] ← ry
                    //   Movmm r2, r3   [rx] ← [ry]

                    case Opcode.MOVI:
                        registers[arg1] = arg2;
                        break;

                    case Opcode.MOVR:
                        registers[arg1] = registers[arg2];
                        break;

                    case Opcode.MOVMR:
                        registers[arg1] = _memory.ReadInt32(registers[arg2]);
                        break;

                    case Opcode.MOVRM:
                        _memory.WriteInt32(registers[arg1], registers[arg2]);
                        break;

                    case Opcode.MOVMM:
                        {
                            int val = _memory.ReadInt32(registers[arg2]);
                            _memory.WriteInt32(registers[arg1], val);
                            break;
                        }

                    // STACK
             

                    case Opcode.PUSHR:
                        registers[SP_REGISTER] -= 4;
                        _memory.WriteInt32(registers[SP_REGISTER], registers[arg1]);
                        break;

                    case Opcode.PUSHI:
                      
                        registers[SP_REGISTER] -= 4;
                        _memory.WriteInt32(registers[SP_REGISTER], arg1);
                        break;

                    case Opcode.POPR:
                        registers[arg1] = _memory.ReadInt32(registers[SP_REGISTER]);
                        registers[SP_REGISTER] += 4;
                        break;

                    case Opcode.POPM:
                        {
                            int val = _memory.ReadInt32(registers[SP_REGISTER]);
                            registers[SP_REGISTER] += 4;
                            _memory.WriteInt32(registers[arg1], val);
                            break;
                        }

                     //Math

                    case Opcode.INCR:
                        registers[arg1]++;
                        break;

                    case Opcode.ADDI:
                        registers[arg1] += arg2;
                        break;

                    case Opcode.ADDR:
                        registers[arg1] += registers[arg2];
                        break;


                    // COMPARISON
                    // Refrence Logic
                    //   rx < y   sign flag set
                    //   rx > y   sign flag clear
                    //   rx == y  zero flag set
  

                    case Opcode.CMPI:
                        {
                            int diff = registers[arg1] - arg2;
                            zeroFlag = (diff == 0);
                            signFlag = (diff < 0);
                            break;
                        }

                    case Opcode.CMPR:
                        {
                            int diff = registers[arg1] - registers[arg2];
                            zeroFlag = (diff == 0);
                            signFlag = (diff < 0);
                            break;
                        }

                    //   Printr r5  — display register as integer
                    //   Printm r5  — display memory at [rx] as integer
                    //   Printcr r1 — display register as character
                    //   Printcm r5 — display memory at [rx] as character


                    case Opcode.PRINTR:
                        Console.WriteLine($"[P{_currentProcessId}] {registers[arg1]}");
                        break;

                    case Opcode.PRINTM:
                        Console.WriteLine($"[P{_currentProcessId}] {_memory.ReadInt32(registers[arg1])}");
                        break;

                    case Opcode.PRINTCR:
                        Console.Write((char)registers[arg1]);
                        break;

                    case Opcode.PRINTCM:
                        Console.Write((char)_memory.ReadByte(registers[arg1]));
                        break;

               
                    // INPUT
                    //   Input  r1 — read 64-bit integer from keyboard into rx
                    //   Inputc r1 — read character from keyboard into rx as ASCII
                    // (We store as 32-bit since registers are 32-bit wide)
        

                    case Opcode.INPUT:
                        Console.Write($"[P{_currentProcessId}] Input (integer): ");
                        if (long.TryParse(Console.ReadLine(), out long inputLong))
                            registers[arg1] = (int)inputLong;
                        else
                        {
                            Console.WriteLine("[CPU] Invalid integer input — defaulting to 0.");
                            registers[arg1] = 0;
                        }
                        break;

                    case Opcode.INPUTC:
                        Console.Write($"[P{_currentProcessId}] Input (char): ");
                        string inputStr = Console.ReadLine();
                        registers[arg1] = (inputStr != null && inputStr.Length > 0) ? (int)inputStr[0] : 0;
                        break;


                    // PRIORITY 
                    //Priority being in the range 1-32, where 1 is the highest priority and 32 is the lowest


                    case Opcode.SETPRIORITY:
                        //CHANGED TO APPLY IMMEDIATLY
                        _activePcb.Priority = Math.Clamp(_memory.ReadInt32(registers[arg1]), 1, 32);
                        _activePcb.BaselinePriority = _activePcb.Priority;
                        break;

                    case Opcode.SETPRIORITYI:
                        _activePcb.Priority = Math.Clamp(arg1, 1, 32);
                        _activePcb.BaselinePriority = _activePcb.Priority;
                        break;


                    // Module 4: SHARED MEMORY
                    // This will return the virtual address of the mapped shared memory region, which the process can then use for reads/writes

                    case Opcode.MAPSHAREDMEM:
                        {
                            int regionId = registers[arg1];
                            int virtAddr = _memory.MapSharedMemory(regionId);
                            registers[arg2] = virtAddr;
                            break;
                        }


                    // LOCKS  (Module 4)
                    // Re-entrant acquire by same process = no-op per spec.
                    // Invalid lock id (outside 1-10) = no-op per spec.

                    case Opcode.ACQUIRELOCK:

                    //The logic fpr ACQUIRELOCK and ACQUIRELOCKI is the same except for how we get the lockId (from register vs immediate), so we can combine them
                    case Opcode.ACQUIRELOCKI:
                        {
                            int lockId = (opCode == Opcode.ACQUIRELOCK) ? registers[arg1] : arg1;
                            if (lockId >= 1 && lockId <= 10)
                            {
                                if (_locks[lockId] == 0)
                                {
                                    _locks[lockId] = _currentProcessId;
                                }
                                else if (_locks[lockId] != _currentProcessId)
                                {
                                  
                                    _blockedByLock = true;
                                    _blockedByLockId = lockId; 
                                    running = false;
                                    jumped = true;
                                }
                                
                            }
                            break;
                        }

                    case Opcode.RELEASELOCK:
                    case Opcode.RELEASELOCKI:
                        {
                            int relId = (opCode == Opcode.RELEASELOCK) ? registers[arg1] : arg1;
                            if (relId >= 1 && relId <= 10 && _locks[relId] == _currentProcessId)
                            {
                                _locks[relId] = 0;
                                LockWaiterMaxPriority[relId] = 0;
                                _justReleasedLockId = relId;
                            }
                            break;
                        }


                    // EVENTS

                    case Opcode.SIGNALEVENT:
                    case Opcode.SIGNALEVENTI:
                        {
                            int eid = (opCode == Opcode.SIGNALEVENT) ? registers[arg1] : arg1;
                            if (eid >= 1 && eid <= 10)
                            {
                                _events[eid] = true;
                                _justSignaledEvent = true;

                            }
                            break;
                        }

                    case Opcode.WAITEVENT:
                    case Opcode.WAITEVENTI:
                        {
                            int eid = (opCode == Opcode.WAITEVENT) ? registers[arg1] : arg1;
                            if (eid >= 1 && eid <= 10)
                            {
                                if (!_events[eid])
                                {
                                    // Not yet signaled
                                    _blockedByEvent = true;
                                    running = false;
                                    jumped = true;
                                }
                                else
                                {
                                    _events[eid] = false; // Consume signal 
                                }
                            }
                            break;
                        }


                    // Module 5
                    // HEAP ALLOCATION
                    //   Alloc rx, ry   
                    //   FreeMemory rx  

                    case Opcode.ALLOC:
                        {
                            int addr = AllocateHeap(registers[arg1]);
                            registers[arg2] = addr;
                            break;
                        }

                    case Opcode.FREEMEMORY:
                        FreeHeap(registers[arg1]);
                        break;

                    default:
                        Console.WriteLine($"[CPU] Unhandled opcode {opCode} at IP={registers[IP_REGISTER]} — skipping.");
                        break;
                }

                if (!jumped)
                    registers[IP_REGISTER] += 9; // Each instruction is exactly 9 bytes
            }

            return cyclesUsed;
        }

        // State shuttled from RunProcess to SaveState
        private int _pendingPriorityChange = -1;
        private int _sleepDuration = 0;
        private int _justReleasedLockId = -1;

        public int JustReleasedLockId => _justReleasedLockId;
        public bool JustSignaledEvent => _justSignaledEvent;

        // Copy CPU state into the PCB

        public void SaveState(PCB pcb, int cyclesUsed)
        {
            Array.Copy(registers, pcb.Registers, registers.Length);
            pcb.SignFlag = signFlag;
            pcb.ZeroFlag = zeroFlag;
            pcb.PageTable = _memory.GetPageTable();
            pcb.ClockCycles += cyclesUsed; // Cumulative — don't overwrite previous total
            pcb.ContextSwitches++;
            pcb.NextHeapAddress = _activeNextHeapAddress;

            if (_halted)
            {
                pcb.State = ProcessState.Terminated;
                // Module 4 release locks
                for (int i = 1; i <= 10; i++)
                {
                    if (_locks[i] == pcb.ProcessId)
                    {
                        _locks[i] = 0;
                        LockWaiterMaxPriority[i] = 0;
                        _justReleasedLockId = i;
                    }
                }
                _halted = false;
            }
            else if (_sleeping)
            {
                pcb.State = ProcessState.WaitingAsleep;
                pcb.SleepCounter = _sleepDuration; // 0 = sleep indefinitely
                _sleeping = false;
                _sleepDuration = 0;
            }
            else if (_blockedByLock)
            {
                pcb.State = ProcessState.WaitingLock;
                pcb.WaitingOnLockId = _blockedByLockId; // Precise lock tracking for priority inversion
                _blockedByLock = false;
                _blockedByLockId = -1;
            }
            else if (_blockedByEvent)
            {
                pcb.State = ProcessState.WaitingEvent;
                _blockedByEvent = false;
            }
            else if (pcb.State == ProcessState.Running)
            {
                pcb.State = ProcessState.Ready;
            }

           // _justReleasedLockId = -1;
        }

        //Restore CPU state from the PCB 
        //Which means loading the process's registers, flags, and memory context so it can resume execution seamlessly
        public void LoadState(PCB pcb)
        {
            //TEST
            _justReleasedLockId = -1;   
            _justSignaledEvent = false; 

            Array.Copy(pcb.Registers, registers, pcb.Registers.Length);
            signFlag = pcb.SignFlag;
            zeroFlag = pcb.ZeroFlag;

            if (pcb.PageTable != null)
                _memory.SetPageTable(pcb.PageTable);

            pcb.State = ProcessState.Running;
            _currentProcessId = pcb.ProcessId;
            _activePcb = pcb;
            pcb.WaitingOnLockId = -1; // No longer blocked on any lock once running

            _activeHeapBlocks = pcb.HeapBlocks;
            _activeNextHeapAddress = pcb.NextHeapAddress;
            _activeHeapEnd = pcb.HeapEnd;

            // Hook page-fault callback to the correct PCB counter
            _memory.OnPageFault = () => pcb.PageFaultCount++;
        }


        // HEAP ALLOCATOR   
        //How this works is the CPU maintains a list of heap blocks for the currently running process, which it updates on Alloc and FreeMemory instructions.
        private int AllocateHeap(int size)
        {
            if (size <= 0) return 0;

            foreach (var block in _activeHeapBlocks)
            {
                if (block.IsFree && block.Size >= size)
                {
                    block.IsFree = false;
                    return block.LogicalAddress;
                }
            }

            int newAddr = _activeNextHeapAddress;
            if (_activeHeapEnd > 0 && newAddr + size > _activeHeapEnd)
            {
                Console.WriteLine($"[CPU] ALLOC failed: heap boundary exceeded (need {size} bytes).");
                return 0;
            }

            _activeHeapBlocks.Add(new HeapBlock { LogicalAddress = newAddr, Size = size, IsFree = false });
            _activeNextHeapAddress += size;
            return newAddr;
        }

        private void FreeHeap(int address)
        {
            foreach (var block in _activeHeapBlocks)
            {
                if (block.LogicalAddress == address && !block.IsFree)
                {
                    block.IsFree = true;
                    return;
                }
            }
            Console.WriteLine($"[CPU] FreeMemory: address {address} not found in heap.");
        }
    }
}