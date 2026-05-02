using System;
using System.IO;

namespace MID
{
    public static class Loader
    {
        public const int STACK_SIZE = 4096; // 4KB 
        public const int GLOBAL_DATA_SIZE = 1024; // 1KB  
        public const int HEAP_SIZE = 512;  

        //REMINDER
        // r11 = IP   (set to 0 — first instruction)
        // r12 = PID  (current process id)
        // r13 = SP   (stack pointer — top of stack)
        // r14 = Global Memory Start address

        private const int IP_REGISTER = 11;
        private const int PID_REGISTER = 12;
        private const int SP_REGISTER = 13;
        private const int GLOBAL_REGISTER = 14;

        //The logic behind LoadProgram is as follows
        //1. Read the assembly file line by line, ignoring comments and blank lines
        //2. Parse each instruction into its opcode and arguments, converting them to the appropriate binary format
        //3. Write the binary instructions sequentially into the memory starting at the specified address
        //4. After loading the code, calculate the memory layout for the process (code, stack, global data, heap) and set the corresponding fields in the PCB
        //5. Initialize the process's registers according to the module 1 specification, ensuring that IP points to the correct start of the code in virtual memory
        //6. Print a summary of the loaded program for verification
    
        public static void LoadProgram(string filename, MemoryManager memory, PCB pcb, int startAddress)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"[Loader] Program file not found: {filename}");

            string[] lines = File.ReadAllLines(filename);
            int currentAddress = startAddress;
            int instructionCount = 0;

            foreach (string line in lines)
            {
                
                string cleanLine = line.Split(';')[0].Trim();
                if (string.IsNullOrEmpty(cleanLine)) continue;

               
                string[] parts = cleanLine.Split(new char[] { ' ', ',' },
                                                 StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                if (!Enum.TryParse(parts[0], ignoreCase: true, out Opcode opCode))
                {
                    Console.WriteLine($"[Loader] Unknown opcode '{parts[0]}' — skipping line.");
                    continue;
                }

                int arg1 = (parts.Length > 1) ? ParseArgument(parts[1]) : 0;
                int arg2 = (parts.Length > 2) ? ParseArgument(parts[2]) : 0;

                // Write 9-byte instruction: 1 byte opcode + 4 bytes arg1 + 4 bytes arg2
                memory.WriteByte(currentAddress, (byte)opCode);
                memory.WriteInt32(currentAddress + 1, arg1);
                memory.WriteInt32(currentAddress + 5, arg2);

                currentAddress += 9;
                instructionCount++;
            }


            //   [ Code ][ Stack ][ Global Data ][ Heap ]

            int codeSize = instructionCount * 9;
            int stackBase = startAddress + codeSize;
            int stackTop = stackBase + STACK_SIZE;    // SP starts here (grows downward)
            int globalBase = stackTop;
            int heapBase = globalBase + GLOBAL_DATA_SIZE;
            int heapEnd = heapBase + HEAP_SIZE;

            pcb.CodeSize = codeSize;
            pcb.StackSize = STACK_SIZE;
            pcb.DataSize = GLOBAL_DATA_SIZE;
            pcb.HeapStart = heapBase;
            pcb.HeapEnd = heapEnd;
            pcb.NextHeapAddress = heapBase;  // Heap bump pointer starts at the base
            pcb.ProcessMemorySize = codeSize + STACK_SIZE + GLOBAL_DATA_SIZE + HEAP_SIZE;

            // Zero out the global data region
            for (int i = 0; i < GLOBAL_DATA_SIZE; i++)
                memory.WriteByte(globalBase + i, 0);


        
            pcb.Registers[IP_REGISTER] = startAddress; // Try this to set IP to the start of the code in virtual memory

            pcb.Registers[PID_REGISTER] = pcb.ProcessId;
            pcb.Registers[SP_REGISTER] = stackTop;
            pcb.Registers[GLOBAL_REGISTER] = globalBase;

            Console.WriteLine($"[Loader] '{filename}' → Process {pcb.ProcessId} | " +
                              $"{instructionCount} instructions | code@{startAddress} | " +
                              $"stack top@{stackTop} | global@{globalBase} | heap@{heapBase}-{heapEnd}");
        }

        // Argument parser
        //How this works is it supports multiple formats for instructions
        
        private static int ParseArgument(string arg)
        {
            arg = arg.Trim();

            // Register: r1 … r14
            if (arg.StartsWith("r", StringComparison.OrdinalIgnoreCase) &&
                arg.Length > 1 && char.IsDigit(arg[1]))
                return int.Parse(arg.Substring(1));

            // Numeric immediate: #10 meaning the literal value 10
            if (arg.StartsWith("#"))
                return int.Parse(arg.Substring(1));

            // Character immediate: @a  meaning ASCII value
            if (arg.StartsWith("@") && arg.Length > 1)
                return (int)arg[1];

            // Raw integer memory address or undecorated constant, may be negative
            if (int.TryParse(arg, out int result))
                return result;

            Console.WriteLine($"[Loader] Could not parse argument '{arg}', defaulting to 0.");
            return 0;
        }
    }
}