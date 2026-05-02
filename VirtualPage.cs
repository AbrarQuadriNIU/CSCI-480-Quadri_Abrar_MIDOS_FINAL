namespace MID
{
    public class VirtualPage
    {
        public int VirtualPageNumber { get; set; }
        public int PhysicalAddress { get; set; }

        // Is the page currently in physical memory?
        public bool IsValid { get; set; }

        // Has this physical page been written to since it was loaded?
        public bool IsDirty { get; set; }

        // Timestamp for the Least Recently Used eviction algorithm
        public int LastAccessed { get; set; }

        // Which process owns this page (for cleanup on process exit)
        public int OwnerProcessId { get; set; }
    }
}