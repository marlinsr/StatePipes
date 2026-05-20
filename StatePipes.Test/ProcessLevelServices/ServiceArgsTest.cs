using StatePipes.ProcessLevelServices;

namespace StatePipes.Test.ProcessLevelServices
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ServiceArgsTest
    {
        [TestMethod]
        public void Constructor_NullArgs_ArgsIsNull()
        {
            var sa = new ServiceArgs(null);
            Assert.IsNull(sa.Args);
        }

        [TestMethod]
        public void Constructor_EmptyList_ArgsIsEmpty()
        {
            var sa = new ServiceArgs([]);
            Assert.IsNotNull(sa.Args);
            Assert.HasCount(0, sa.Args);
        }

        [TestMethod]
        public void Constructor_ValidArgs_StoresArgs()
        {
            var sa = new ServiceArgs(["--loglevel=Debug", "--postfix=abc"]);
            Assert.HasCount(2, sa.Args!);
        }

        [TestMethod]
        public void Constructor_ArgMissingDelimiter_ThrowsArgumentException()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new ServiceArgs(["--loglevel"]));
        }

        [TestMethod]
        public void GetArgValue_NullArgs_ReturnsNull()
        {
            var sa = new ServiceArgs(null);
            Assert.IsNull(sa.GetArgValue(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void GetArgValue_EmptyPrefix_ReturnsNull()
        {
            var sa = new ServiceArgs(["--loglevel=Debug"]);
            Assert.IsNull(sa.GetArgValue(string.Empty));
        }

        [TestMethod]
        public void GetArgValue_MatchingArg_ReturnsValue()
        {
            var sa = new ServiceArgs(["--loglevel=Debug"]);
            Assert.AreEqual("Debug", sa.GetArgValue(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void GetArgValue_CaseInsensitiveMatch_ReturnsValue()
        {
            var sa = new ServiceArgs(["--LOGLEVEL=Debug"]);
            Assert.AreEqual("Debug", sa.GetArgValue(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void GetArgValue_ArgNotPresent_ReturnsNull()
        {
            var sa = new ServiceArgs(["--postfix=abc"]);
            Assert.IsNull(sa.GetArgValue(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void GetArgValue_ValueContainsEqualsSign_ReturnsEverythingAfterFirstEquals()
        {
            var sa = new ServiceArgs(["--postfix=a=b"]);
            Assert.AreEqual("a=b", sa.GetArgValue(ServiceArgs.PostFix));
        }

        [TestMethod]
        public void GetArgName_ReturnsNameBeforeDelimiter()
        {
            var sa = new ServiceArgs(["--loglevel=Debug"]);
            Assert.AreEqual("--loglevel", sa.GetArgName("--loglevel=Debug"));
        }

        [TestMethod]
        public void ContainsArgName_PresentArg_ReturnsTrue()
        {
            var sa = new ServiceArgs(["--loglevel=Debug"]);
            Assert.IsTrue(sa.ContainsArgName(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void ContainsArgName_AbsentArg_ReturnsFalse()
        {
            var sa = new ServiceArgs(["--postfix=abc"]);
            Assert.IsFalse(sa.ContainsArgName(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void ContainsArgName_NullArgs_ReturnsFalse()
        {
            var sa = new ServiceArgs(null);
            Assert.IsFalse(sa.ContainsArgName(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void Remove_ExistingArg_RemovesIt()
        {
            var sa = new ServiceArgs(["--loglevel=Debug", "--postfix=abc"]);
            var result = sa.Remove(ServiceArgs.LogLevelArg);
            Assert.IsFalse(result.ContainsArgName(ServiceArgs.LogLevelArg));
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.PostFix));
        }

        [TestMethod]
        public void Remove_AbsentArg_ResultUnchanged()
        {
            var sa = new ServiceArgs(["--postfix=abc"]);
            var result = sa.Remove(ServiceArgs.LogLevelArg);
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.PostFix));
            Assert.HasCount(1, result.Args!);
        }

        [TestMethod]
        public void Remove_NullArgs_ResultHasNullArgs()
        {
            var sa = new ServiceArgs(null);
            var result = sa.Remove(ServiceArgs.LogLevelArg);
            Assert.IsNull(result.Args);
        }

        [TestMethod]
        public void GetArgsNotFoundIn_ReturnsArgsMissingFromOther()
        {
            var sa = new ServiceArgs(["--loglevel=Debug", "--postfix=abc"]);
            var other = new ServiceArgs(["--loglevel=Info"]);
            var diff = sa.GetArgsNotFoundIn(other);
            Assert.IsFalse(diff.ContainsArgName(ServiceArgs.LogLevelArg));
            Assert.IsTrue(diff.ContainsArgName(ServiceArgs.PostFix));
        }

        [TestMethod]
        public void GetArgsNotFoundIn_AllArgsInOther_ReturnsEmpty()
        {
            var sa = new ServiceArgs(["--loglevel=Debug"]);
            var other = new ServiceArgs(["--loglevel=Info"]);
            var diff = sa.GetArgsNotFoundIn(other);
            Assert.HasCount(0, diff.Args!);
        }

        [TestMethod]
        public void Concat_BothHaveArgs_CombinesList()
        {
            var a = new ServiceArgs(["--loglevel=Debug"]);
            var b = new ServiceArgs(["--postfix=abc"]);
            var result = a.Concat(b);
            Assert.HasCount(2, result.Args!);
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.LogLevelArg));
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.PostFix));
        }

        [TestMethod]
        public void Concat_NullSelfArgs_ReturnsOtherArgs()
        {
            var a = new ServiceArgs(null);
            var b = new ServiceArgs(["--postfix=abc"]);
            var result = a.Concat(b);
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.PostFix));
        }

        [TestMethod]
        public void Concat_NullOtherArgs_ReturnsSelfArgs()
        {
            var a = new ServiceArgs(["--loglevel=Debug"]);
            var b = new ServiceArgs(null);
            var result = a.Concat(b);
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void Merge_DominantArgOverridesSelf()
        {
            var self = new ServiceArgs(["--loglevel=Debug"]);
            var dominant = new ServiceArgs(["--loglevel=Warning"]);
            var result = self.Merge(dominant);
            Assert.AreEqual("Warning", result.GetArgValue(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void Merge_NonOverlappingArgsFromBothIncluded()
        {
            var self = new ServiceArgs(["--postfix=abc"]);
            var dominant = new ServiceArgs(["--loglevel=Warning"]);
            var result = self.Merge(dominant);
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.PostFix));
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void Merge_NullSelf_ReturnsDominant()
        {
            var self = new ServiceArgs(null);
            var dominant = new ServiceArgs(["--loglevel=Warning"]);
            var result = self.Merge(dominant);
            Assert.AreEqual("Warning", result.GetArgValue(ServiceArgs.LogLevelArg));
        }

        [TestMethod]
        public void Merge_NullDominant_ReturnsCopyOfSelf()
        {
            var self = new ServiceArgs(["--postfix=abc"]);
            var dominant = new ServiceArgs(null);
            var result = self.Merge(dominant);
            Assert.IsTrue(result.ContainsArgName(ServiceArgs.PostFix));
        }
    }
}
