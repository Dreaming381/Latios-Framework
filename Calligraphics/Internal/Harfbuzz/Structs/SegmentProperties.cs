namespace Latios.Calligraphics.HarfBuzz
{
    internal unsafe struct SegmentProperties
    {
        public Direction direction;
        public Script script;
        public Language language;
        ///*< private >*/
        void* reserved1;
        void* reserved2;
        public SegmentProperties(Direction direction, Script script, Language language)
        {
            this.direction = direction;
            this.script = script;
            this.language = language;
            reserved1 = default(void*);
            reserved2 = default(void*);
        }
        public override string ToString()
        {
            return $"Direction: {direction} Script: {script} Language: {language}";
        }
    }
}
