using System;

namespace readboard
{
    internal sealed class FoxWindowBinding
    {
        public IntPtr BoardHandle { get; set; }

        public IntPtr TitleSourceHandle { get; set; }

        public FoxWindowKind WindowKind { get; set; }
    }
}
