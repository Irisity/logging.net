namespace Logging.Net.Abstractions.Helpers
{
    internal class NameAndLevelHandler
    {
        private int level;
        private string name;

        public NameAndLevelHandler()
        {
        }

        public NameAndLevelHandler(NameAndLevelHandler nameAndLevelHandler)
        {
            level = nameAndLevelHandler.level;
            name = nameAndLevelHandler.name;
        }

        public NameAndLevelHandler AddName(string name)
        {
            var copy = new NameAndLevelHandler(this);
            copy.name = copy.name == null ? name : copy.name + "/" + name;
            return copy;
        }

        public NameAndLevelHandler AddLevel(int level)
        {
            var copy = new NameAndLevelHandler(this);
            copy.level += level;
            return copy;
        }

        public string Name => name;

        public int Level => level;
    }
}
