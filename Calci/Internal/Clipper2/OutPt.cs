namespace Latios.Calci.Clipper2
{
    // OutPt: vertex data structure for clipping solutions
    internal struct OutPt
    {
        public long2 pt;
        public int   next;
        public int   prev;
        public int   outrec;
        public int   horz;
        public OutPt(long2 pt, int outrec)
        {
            this.pt     = pt;
            this.outrec = outrec;
            next        = -1;
            prev        = -1;
            horz        = -1;
        }
    };
}  //namespace

