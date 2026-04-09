namespace OzzieAI.Agentica
{

    using System;

    /// <summary>
    /// ✨ Beautiful Console Logger ✨
    /// 
    /// A simple helper that adds timestamps to every log message.
    /// Makes it easy to see the exact order and timing of events.
    /// </summary>
    public static class ConsoleLogger
    {
        /// <summary>
        /// Writes a message with a timestamp in cyan color.
        /// </summary>
        public static void WriteLine(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{timestamp}] {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// Writes a colored message with timestamp.
        /// </summary>
        public static void WriteLine(string message, ConsoleColor color)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = color;
            Console.WriteLine($"[{timestamp}] {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// Writes a debug message (gray).
        /// </summary>
        public static void Debug(string message)
        {
            WriteLine($"[DEBUG] {message}", ConsoleColor.Gray);
        }

        /// <summary>
        /// Writes a success message (green).
        /// </summary>
        public static void Success(string message)
        {
            WriteLine($"✅ {message}", ConsoleColor.Green);
        }

        /// <summary>
        /// Writes a warning message (yellow).
        /// </summary>
        public static void Warning(string message)
        {
            WriteLine($"⚠️ {message}", ConsoleColor.Yellow);
        }

        /// <summary>
        /// Writes an error message (red).
        /// </summary>
        public static void Error(string message)
        {
            WriteLine($"❌ {message}", ConsoleColor.Red);
        }
    }
}