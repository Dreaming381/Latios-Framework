namespace Latios.Calci.Clipper2
{
    internal struct Vertex
    {
        public long2       pt;
        public int         next;
        public int         prev;
        public VertexFlags flags;
        public Vertex(long2 pt, VertexFlags flags, int prev)
        {
            this.pt    = pt;
            this.flags = flags;
            next       = -1;
            this.prev  = prev;
        }
        public override int GetHashCode()
        {
            int hash = 17;
            hash     = hash * 29 + pt.GetHashCode();
            hash     = hash * 29 + (int)flags;
            return hash;
        }
    };
}  //namespace

