using System;
using System.Collections.Generic;
using System.Linq;

namespace MID
{
    public class Scheduler
    {
        private List<PCB> _processTable = new List<PCB>();
        private CPU _cpu;
        private PCB _currentProcess;

        // Try tracking separately so we never remove it from the roster
        private PCB _idleProcess;

        public Scheduler(CPU cpu)
        {
            _cpu = cpu;
        }

        // Registers a new user process
        public void AddProcess(PCB pcb)
        {
            pcb.State = ProcessState.Ready;
            _processTable.Add(pcb);
        }

        // Registers the idle process (lowest priority, runs forever)
        // Call this once with the pre-loaded idle PCB before calling Run()
        public void SetIdleProcess(PCB idle)
        {
            idle.Priority = 1;           //  idle always has the lowest priority
            idle.BaselinePriority = 1;
            idle.TimeQuantum = 5;        //  idle quantum is 5
            idle.State = ProcessState.Ready;
            _idleProcess = idle;
            _processTable.Add(idle);
        }

        // Main OS loop
        public void Run()
        {

            while (true)
            {
                // 1. Tick down sleeping processes and wake any whose sleep has expired

                foreach (var p in _processTable)
                {
                    if (p.State == ProcessState.WaitingAsleep)
                    {
                        // Sleep rx with rx == 0 means sleep indefinitely
                        // Only count down (and wake) if the counter is positive
                        if (p.SleepCounter > 0)
                        {
                            p.SleepCounter--;
                            if (p.SleepCounter == 0)
                            {
                                p.State = ProcessState.Ready;
                                Console.WriteLine($"[OS] Process {p.ProcessId} woke up from sleep.");
                            }
                        }
                        // SleepCounter == 0 on entry = indefinite sleep; leave it alone
                    }
                }

                // 2. Pick the highest-priority Ready process  
                //The ,where logic ensures we only consider processes that are actually ready to run, excluding those that are sleeping, blocked on locks, or waiting for events.

                _currentProcess = _processTable
                    .Where(p => p.State == ProcessState.Ready)
                    .OrderByDescending(p => p.Priority)
                    .FirstOrDefault();

                // If nothing at all is runnable (all sleeping or blocked), gg
                // unless the idle process exists. The idle process should always be Ready
                // so this case only hits if the idle process was never set.
                if (_currentProcess == null)
                {
                    bool anyStillAlive = _processTable.Any(p =>
                        p.State == ProcessState.WaitingAsleep ||
                        p.State == ProcessState.WaitingLock ||
                        p.State == ProcessState.WaitingEvent);

                    if (anyStillAlive)
                        continue; // Spin and wait — real OS would run idle here
                    else
                        break;    // Nothing left at all
                }

                Console.WriteLine($"\n[OS] Swapping IN Process {_currentProcess.ProcessId} (Priority: {_currentProcess.Priority})");

                // 3. Context switch IN  
                _cpu.LoadState(_currentProcess);

                // 4. Run 
                int cyclesUsed = _cpu.RunProcess(_currentProcess.TimeQuantum);

                // 5. Context switch OUT 
                _cpu.SaveState(_currentProcess, cyclesUsed);

                //  6. Handle the just-released lock (if any)
                // Try to wake up the single highest-priority process waiting on that lock, if any.
                int releasedLock = _cpu.JustReleasedLockId;
                if (releasedLock >= 1 && releasedLock <= 10)
                {
                    WakeHighestPriorityLockWaiter(releasedLock);
                }

                // 6b? Handle just-signaled events 
                //Try to wake up all processes waiting on the event that was just signaled, if any. Let's wake them all and let them compete for the CPU on the next round.
                if (_cpu.JustSignaledEvent)
                {
                    foreach (var p in _processTable.Where(x => x.State == ProcessState.WaitingEvent))
                    {
                        p.State = ProcessState.Ready;
                        Console.WriteLine($"[OS] Event signaled — waking Process {p.ProcessId}.");
                    }
                }

                //  7. Handle priority inversion 
                ApplyPriorityInversion();

                //  8. Handle process termination 
                if (_currentProcess.State == ProcessState.Terminated)
                {
                    PrintExitReport(_currentProcess);

                    // If the idle process terminates somehow, don't remove it
                    // (per spec it never exits, but be safe)
                    if (_currentProcess != _idleProcess)
                        _processTable.Remove(_currentProcess);

                    // Check if only the idle process remains
                    bool onlyIdleLeft = _idleProcess != null &&
                                        _processTable.All(p => p == _idleProcess);
                    if (onlyIdleLeft)
                        break;
                }
                else
                {
                    // Move to back of the list so other equal-priority processes get a turn
                    _processTable.Remove(_currentProcess);
                    _processTable.Add(_currentProcess);
                }
            }

            Console.WriteLine("\n[OS] All processes completed. System halting.");
        }


        // Try to wake the single highest-priority process waiting on the specified lock, if any?
        private void WakeHighestPriorityLockWaiter(int lockId)
        {
            PCB highestWaiter = _processTable
                .Where(p => p.State == ProcessState.WaitingLock)
                .OrderByDescending(p => p.Priority)
                .FirstOrDefault();

            if (highestWaiter != null)
            {
                highestWaiter.State = ProcessState.Ready;
                Console.WriteLine($"[OS] Lock {lockId} released — waking Process {highestWaiter.ProcessId} (Priority {highestWaiter.Priority}).");
                // All other WaitingLock processes remain blocked until their next chance
            }
        }

        //Try to apply priority inversion by boosting the priority of any process that is blocking a higher-priority process. Also restore priorities when the boost is no longer needed
        private void ApplyPriorityInversion()
        {
            foreach (var blocked in _processTable.Where(p => p.State == ProcessState.WaitingLock))
            {
                int lockId = blocked.WaitingOnLockId;
                if (lockId < 1 || lockId > 10) continue;

                int ownerPid = _cpu.GetLockOwner(lockId);
                if (ownerPid == 0) continue;

                PCB owner = _processTable.FirstOrDefault(p => p.ProcessId == ownerPid);
                if (owner == null) continue;

                if (blocked.Priority > owner.Priority)
                {
                    Console.WriteLine($"[OS] Priority Inversion: bumping Process {owner.ProcessId} " +
                                      $"from priority {owner.Priority} to {blocked.Priority} " +
                                      $"(blocked by Process {blocked.ProcessId} on lock {lockId}).");
                    owner.Priority = blocked.Priority;
                }
            }

            // Restore priority for any process whose boost is no longer needed.
            // A boost is no longer needed when no higher-priority process is blocked
            // specifically on a lock this process holds.
            foreach (var p in _processTable)
            {
                if (p.Priority <= p.BaselinePriority) continue;

                bool stillNeeded = false;
                for (int lockId = 1; lockId <= 10; lockId++)
                {
                    if (!_cpu.IsLockOwnedBy(lockId, p.ProcessId)) continue;

                    // FIX: Check only processes waiting specifically on THIS lock
                    stillNeeded = _processTable.Any(w =>
                        w.State == ProcessState.WaitingLock &&
                        w.WaitingOnLockId == lockId &&
                        w.Priority > p.BaselinePriority);

                    if (stillNeeded) break;
                }

                if (!stillNeeded)
                {
                    Console.WriteLine($"[OS] Restoring Process {p.ProcessId} priority " +
                                      $"from {p.Priority} back to baseline {p.BaselinePriority}.");
                    p.Priority = p.BaselinePriority;
                }
            }
        }

        private void PrintExitReport(PCB pcb)
        {
            Console.WriteLine($"\n[OS] ===== Process {pcb.ProcessId} Exit Report =====");
            Console.WriteLine($"       Page Faults:      {pcb.PageFaultCount}");
            Console.WriteLine($"       Context Switches: {pcb.ContextSwitches}");
            Console.WriteLine($"       Clock Cycles:     {pcb.ClockCycles}");
            Console.WriteLine($"[OS] =============================================\n");
        }
    }
}