namespace Latios.Calligraphics.RichText
{
    internal struct RectOffsets
    {
        public float left { get { return m_Left; } set { m_Left = value; } }

        public float right { get { return m_Right; } set { m_Right = value; } }

        public float top { get { return m_Top; } set { m_Top = value; } }

        public float bottom { get { return m_Bottom; } set { m_Bottom = value; } }

        public float horizontal { get { return m_Left; } set { m_Left = value; m_Right = value; } }

        public float vertical { get { return m_Top; } set { m_Top = value; m_Bottom = value; } }

        /// <summary>
        ///
        /// </summary>
        public static RectOffsets zero { get { return k_ZeroOffset; } }

        // =============================================
        // Private backing fields for public properties.
        // =============================================

        float m_Left;
        float m_Right;
        float m_Top;
        float m_Bottom;

        static readonly RectOffsets k_ZeroOffset = new RectOffsets(0F, 0F, 0F, 0F);

        /// <summary>
        ///
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="top"></param>
        /// <param name="bottom"></param>
        public RectOffsets(float left, float right, float top, float bottom)
        {
            m_Left   = left;
            m_Right  = right;
            m_Top    = top;
            m_Bottom = bottom;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="horizontal"></param>
        /// <param name="vertical"></param>
        public RectOffsets(float horizontal, float vertical)
        {
            m_Left   = horizontal;
            m_Right  = horizontal;
            m_Top    = vertical;
            m_Bottom = vertical;
        }

        public static bool operator ==(RectOffsets lhs, RectOffsets rhs)
        {
            return lhs.m_Left == rhs.m_Left &&
                   lhs.m_Right == rhs.m_Right &&
                   lhs.m_Top == rhs.m_Top &&
                   lhs.m_Bottom == rhs.m_Bottom;
        }

        public static bool operator !=(RectOffsets lhs, RectOffsets rhs)
        {
            return !(lhs == rhs);
        }

        public static RectOffsets operator *(RectOffsets a, float b)
        {
            return new RectOffsets(a.m_Left * b, a.m_Right * b, a.m_Top * b, a.m_Bottom * b);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public bool Equals(RectOffsets other)
        {
            return base.Equals(other);
        }
    }
}

