using System;

namespace LinkerPlayer.Core;

    [Serializable]
    public class MediaFileException : Exception
    {
        public MediaFileException()
        {
        }

        public MediaFileException(string message)
            : base(message)
        {
        }

        public MediaFileException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
