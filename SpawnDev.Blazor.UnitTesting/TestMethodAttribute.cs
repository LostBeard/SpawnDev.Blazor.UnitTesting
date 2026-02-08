namespace SpawnDev.Blazor.UnitTesting
{
    /// <summary>
    /// Used to mark test methods for unit testing
    /// </summary>
    public class TestMethodAttribute : Attribute
    {
        /// <summary>
        /// If true, the test requires https to run and will automatically be marked as success on http connections to prevent test failure
        /// </summary>
        public bool RequiresHttps { get; set; }
        /// <summary>
        /// Test method name
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Maximum time in milliseconds to wait for the test to complete.
        /// A value of 0 (default) uses the runner's DefaultTimeout.
        /// </summary>
        public int Timeout { get; set; }
        /// <summary>
        /// New instance
        /// </summary>
        /// <param name="name"></param>
        public TestMethodAttribute(string name = "")
        {
            Name = name;
        }
    }
}
