namespace SpawnDev.Blazor.UnitTesting
{
    /// <summary>
    /// Can be thrown to end a test indicating the test can not continue do to being unsupported.<br/>
    /// This is not counted as an actual test failure
    /// </summary>
    public class UnsupportedTestException : Exception
    {
        /// <summary>
        /// New instance
        /// </summary>
        /// <param name="message"></param>
        public UnsupportedTestException(string? message = null) : base(message) { }
    }
}
