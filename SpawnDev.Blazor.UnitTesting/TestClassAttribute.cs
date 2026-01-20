namespace SpawnDev.Blazor.UnitTesting
{
    /// <summary>
    /// Used to mark test classes for unit testing
    /// </summary>
    public class TestClassAttribute : Attribute
    {
        /// <summary>
        /// If true, the test requires https to run andwill automatically bemarked as success on http connections to prevent test failure
        /// </summary>
        public bool RequiresHttps { get; set; }
        /// <summary>
        /// Test method name
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// New instance
        /// </summary>
        /// <param name="name"></param>
        public TestClassAttribute(string name = "")
        {
            Name = name;
        }
    }
}
