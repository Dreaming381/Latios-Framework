namespace Latios.Calci.Clipper2
{
    ///////////////////////////////////////////////////////////////////
    // Important: UP and DOWN here are premised on Y-axis positive down
    // displays, which is the orientation used in Clipper's development.
    ///////////////////////////////////////////////////////////////////
    internal enum JoinWith
    {
        None,
        Left,
        Right
    };

    internal struct Active
    {
        public long2  bot;
        public long2  top;
        public long   curX;  // current (updated at every new scanline)
        public double dx;
        public int    windDx;  // 1 or -1 depending on winding direction
        public int    windCount;
        public int    windCount2;  // winding count of the opposite polytype
        public int    outrec;

        // AEL: 'active edge list' (Vatti's AET - active edge table)
        //     a linked list of all edges (from left to right) that are present
        //     (or 'active') within the current scanbeam (a horizontal 'beam' that
        //     sweeps from bottom to top over the paths in the clipping operation).
        public int prevInAEL;
        public int nextInAEL;

        // SEL: 'sorted edge list' (Vatti's ST - sorted table)
        //     linked list used when sorting edges into their new positions at the
        //     top of scanbeams, but also (re)used to process horizontals.
        public int         prevInSEL;
        public int         nextInSEL;
        public int         jump;
        public int         vertexTop;
        public LocalMinima localMin;
        internal bool      isLeftBound;
        internal JoinWith  joinWith;
    };
}  //namespace

