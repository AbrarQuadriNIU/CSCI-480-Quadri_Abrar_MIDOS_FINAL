using System;
using System.IO;

namespace MID
{
    class Program
    {
        // Usage: MID <virtualMemorySizeInBytes> <program1.txt> [program2.txt] 
        //The ram sizes to test with are 65536 (64KB) and 8192 (8KB). 
        static void Main(string[] args)
        {
            // CHANGED TO TAKE TWO ARGUEMENTS 
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: MID <memorySize> <program1.txt> [program2.txt] ...");
                Console.WriteLine("Example: MID 65536 prog1.txt prog2.txt");
                return;
            }

            if (!int.TryParse(args[0], out int memorySize) || memorySize <= 0)
            {
                Console.WriteLine($"Error: '{args[0]}' is not a valid memory size.");
                return;
            }

            Console.WriteLine("MIDOS Starting....");
            Console.WriteLine($"[OS] Physical memory: {memorySize} bytes");

            //  Boot the hardware 
            MemoryManager memory = new MemoryManager(memorySize);
            CPU cpu = new CPU(memory);
            Scheduler os = new Scheduler(cpu);

            // If this loops forever, it means the idle process is not loading correctly.  
            const string IDLE_FILE = "idle.txt";
            if (!File.Exists(IDLE_FILE))
            {
                // Create a default idle.txt if it doesn't exist so the OS can boot
                Console.WriteLine($"[OS] Warning: {IDLE_FILE} not found. Creating a default idle process.");
                File.WriteAllLines(IDLE_FILE, new[]
                {
                    "; idle.txt — the idle process. Lowest priority, never exits.",
                    "movi r1, #20",   // Load the value 20 into r1
                    "printr r1",      // Print r1  
                    "jmpi #-18",      // Jump back 2 instructions (2 * 9 bytes = 18 bytes) 
                });
            }

            int idleStartAddress = 0; // Idle process always loads at base address 0 of its own page
            PCB idlePcb = new PCB(processId: 0, priority: 1);
            idlePcb.PageTable = new int[memory.PhysicalPages];
            for (int i = 0; i < memory.PhysicalPages; i++)
                idlePcb.PageTable[i] = i * MemoryManager.PAGE_SIZE;

            memory.SetPageTable(idlePcb.PageTable);
            Loader.LoadProgram(IDLE_FILE, memory, idlePcb, idleStartAddress);
            os.SetIdleProcess(idlePcb);
            Console.WriteLine($"[OS] Idle process loaded.");

            // Load user programs from command-line arguments
            // Each program gets its own PCB and is laid out sequentially in memory.
            const int MAX_PROGRAM_MEMORY = 8192; // 8KB per process slot (code + stack + data + heap)
            int nextStartAddress = MAX_PROGRAM_MEMORY; // Reserve first 8KB for idle
            int nextProcessId = 1;

            for (int i = 1; i < args.Length; i++)
            {
                string programFile = args[i];

                if (!File.Exists(programFile))
                {
                    // will this work locally who knows
                    string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\", programFile);

                    if (File.Exists(fallbackPath))
                    {
                        
                        programFile = fallbackPath;
                    }
                    else
                    {
                        Console.WriteLine($"[OS] Warning: Program file '{args[i]}' not found in bin or project root — skipping.");
                        continue;
                    }
                }

                //Create PCB for the new process
                PCB pcb = new PCB(processId: nextProcessId, priority: 16);
                pcb.PageTable = new int[memory.PhysicalPages];
                for (int p = 0; p < memory.PhysicalPages; p++)
                    pcb.PageTable[p] = p * MemoryManager.PAGE_SIZE;

                memory.SetPageTable(pcb.PageTable);
                Loader.LoadProgram(programFile, memory, pcb, nextStartAddress);

                os.AddProcess(pcb);
                Console.WriteLine($"[OS] Process {nextProcessId} registered from '{programFile}'.");

                nextStartAddress += MAX_PROGRAM_MEMORY;
                nextProcessId++;
            }

            // Hand control to the scheduler  
            Console.WriteLine("\n[OS] All programs loaded. Starting scheduler...\n");
            os.Run();

            Console.WriteLine("\n[OS] Press any key to exit.");
            Console.ReadKey();
        }
    }
}