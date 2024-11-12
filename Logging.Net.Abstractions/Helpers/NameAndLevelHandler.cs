using System.Text;

namespace Logging.Net.Abstractions.Helpers
{
    public class NameAndLevelHandler
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
            if (copy.name == null)
                copy.name = name;
            else
            {
                var sb = new StringBuilder(copy.name, copy.name.Length + 1 + name.Length);
                sb.Append('/');
                sb.Append(name);
                copy.name = sb.ToString();
            }
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