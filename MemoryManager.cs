using System;
using System.Collections.Generic;
using System.Linq;

namespace MID
{
    public class MemoryManager
    {
        //RAM 
        private byte[] _physicalMemory;

        //May be used by shared memory
        //What a legacy table simply is is a mapping of page number to physical address
        //So if the process tries to access virtual page 3, the MMU looks at _pageTable[3] to find the corresponding physical address in RAM.
        private int[] _pageTable;

        // Module 4: OS-Level Shared Pages
        private int[] _sharedPhysicalPages = new int[11];

        // Module 6: Virtual Memory with Paging
        public const int PAGE_SIZE = 256;
        public int PhysicalPages { get; private set; }
        public const int VIRTUAL_PAGES = 512;  // 128KB Virtual Space (double the physical RAM)

        private VirtualPage[] _virtualPageTable = new VirtualPage[VIRTUAL_PAGES];

        // Simulated hard drive for swapped-out pages. Key = virtual page number, Value = page data
        private Dictionary<int, byte[]> _hardDrive = new Dictionary<int, byte[]>();

        // Clock used for LRU tracking — incremented on every memory access
        private int _memoryClock = 0;


        // The CPU sets this delegate before running each process.
        public Action OnPageFault { get; set; }

        // Constructor
        public MemoryManager(int byteSize)
        {
            _physicalMemory = new byte[byteSize];

            PhysicalPages = byteSize / PAGE_SIZE;

            // Try initializing the legacy page table, but if the byteSize is too small to even support one page, just leave it null and let the OS handle it with the virtual page table.
            int numPages = byteSize / PAGE_SIZE;
            _pageTable = new int[numPages];

            // Mapping for the legacy page table
            for (int i = 0; i < numPages; i++)
            {
                _pageTable[i] = i * PAGE_SIZE;
            }

            // Pre-allocate the 10 shared memory regions at the top of physical RAM
            for (int i = 1; i <= 10; i++)
            {
                _sharedPhysicalPages[i] = (numPages - i) * PAGE_SIZE;
            }

            // Initialize the virtual page table
            for (int i = 0; i < VIRTUAL_PAGES; i++)
            {
                _virtualPageTable[i] = new VirtualPage { VirtualPageNumber = i };

                if (i < PhysicalPages)
                {
                    _virtualPageTable[i].IsValid = true;
                    _virtualPageTable[i].IsDirty = false;
                    _virtualPageTable[i].PhysicalAddress = i * PAGE_SIZE;
                }
                else
                {
                    // Pages beyond physical RAM exist only in virtual space
                    _virtualPageTable[i].IsValid = false;
                }
            }
        }

        // Read and Wryte functions 

        // What this does is take a virtual address, figure out which virtual page it's on, check if that page is currently valid (in RAM)
        // if not, handle the page fault to swap it in
        // Then it uses the physical address from the virtual page table to read or write the byte in physical memory
        public byte ReadByte(int virtualAddress)
        {
            int virtualPageNum = virtualAddress / PAGE_SIZE;
            int offset = virtualAddress % PAGE_SIZE;

            if (virtualPageNum < 0 || virtualPageNum >= VIRTUAL_PAGES)
                throw new Exception($"[MMU] Virtual address {virtualAddress} is out of bounds.");

            VirtualPage page = _virtualPageTable[virtualPageNum];

            if (!page.IsValid)
            {
                HandlePageFault(virtualPageNum);
            }

            page.LastAccessed = _memoryClock++;
            return _physicalMemory[page.PhysicalAddress + offset];
        }


        // Similar to ReadByte, but also sets the dirty bit since we're modifying the page.
        // Dirty bit meaning the page has been modified since it was loaded into RAM, so if we need to kick it later we can write it back on the disk 
        public void WriteByte(int virtualAddress, byte value)
        {
            int virtualPageNum = virtualAddress / PAGE_SIZE;
            int offset = virtualAddress % PAGE_SIZE;

            if (virtualPageNum < 0 || virtualPageNum >= VIRTUAL_PAGES)
                throw new Exception($"[MMU] Virtual address {virtualAddress} is out of bounds.");

            VirtualPage page = _virtualPageTable[virtualPageNum];

            if (!page.IsValid)
            {
                HandlePageFault(virtualPageNum);
            }

            page.LastAccessed = _memoryClock++;
            page.IsDirty = true;
            _physicalMemory[page.PhysicalAddress + offset] = value;
        }

        // Read 4 bytes and assemble them into a 32-bit integer.
        // Done byte-by-byte so we correctly handle values that straddle a page boundary.
        public int ReadInt32(int address)
        {
            byte b0 = ReadByte(address);
            byte b1 = ReadByte(address + 1);
            byte b2 = ReadByte(address + 2);
            byte b3 = ReadByte(address + 3);
            return BitConverter.ToInt32(new byte[] { b0, b1, b2, b3 }, 0);
        }

        public void WriteInt32(int address, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            WriteByte(address, bytes[0]);
            WriteByte(address + 1, bytes[1]);
            WriteByte(address + 2, bytes[2]);
            WriteByte(address + 3, bytes[3]);
        }

        //Page Fault Handler

        private void HandlePageFault(int targetVirtualPageNum)
        {
            Console.WriteLine($"[MMU] PAGE FAULT, swapping Virtual Page {targetVirtualPageNum} into RAM...");

            // Notify the CPU so the active PCB's PageFaultCount gets incremented
            OnPageFault?.Invoke();

            // Find the least recently used page currently in physical memory
            VirtualPage lruPage = _virtualPageTable
                .Where(p => p.IsValid)
                .OrderBy(p => p.LastAccessed)
                .First();

            // Only write back to disk if the page has been modified 
            if (lruPage.IsDirty)
            {
                byte[] pageData = new byte[PAGE_SIZE];
                Array.Copy(_physicalMemory, lruPage.PhysicalAddress, pageData, 0, PAGE_SIZE);
                _hardDrive[lruPage.VirtualPageNumber] = pageData;
            }

            // Evict the LRU page
            lruPage.IsValid = false;
            lruPage.IsDirty = false;

            // Move the target page into the freed physical slot
            VirtualPage targetPage = _virtualPageTable[targetVirtualPageNum];
            targetPage.PhysicalAddress = lruPage.PhysicalAddress;
            targetPage.IsValid = true;
            targetPage.IsDirty = false;

            // Restore page data from disk if it was previously swapped out
            if (_hardDrive.ContainsKey(targetVirtualPageNum))
            {
                Array.Copy(_hardDrive[targetVirtualPageNum], 0, _physicalMemory, targetPage.PhysicalAddress, PAGE_SIZE);
            }
            else
            {
                // Fresh page — zero it out so a process doesn't see another process's old data
                Array.Clear(_physicalMemory, targetPage.PhysicalAddress, PAGE_SIZE);
            }
        }

        // Context Switching Support

        public int[] GetPageTable()
        {
            if (_pageTable == null) return null;
            return (int[])_pageTable.Clone();
        }

        public void SetPageTable(int[] newTable)
        {
            if (newTable != null)
            {
                _pageTable = (int[])newTable.Clone();
            }
        }

        //  Shared Memory

        // Maps one of the 10 OS-provided shared regions into the current process's address space.
        // Returns the virtual address the process should use to access it, or -1 on failure.
        public int MapSharedMemory(int regionId)
        {
            // The OS only supports 10 shared regions 
            if (regionId < 1 || regionId > 10) return -1;

            // Dedicate the highest 10 Virtual Pages to act as the Shared Regions
            int sharedVirtualPage = VIRTUAL_PAGES - 11 + regionId;

            // Return the exact global virtual address for this region
            return sharedVirtualPage * PAGE_SIZE;
        }

        public int Size => VIRTUAL_PAGES * PAGE_SIZE;
    }
}