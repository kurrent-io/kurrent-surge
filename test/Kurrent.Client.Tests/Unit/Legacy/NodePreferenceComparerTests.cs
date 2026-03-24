// using Kurrent.Testing.TUnit;
// using KurrentDB.Client;
//
// using static KurrentDB.Client.ClusterMessages.VNodeState;
//
// namespace Kurrent.Client.Tests.Legacy;
//
// [Category("Legacy")]
// public class NodePreferenceComparerTests {
// 	public class LeaderTestCases : TestCaseGenerator<NodePreferenceComparerTestCase> {
// 		protected override IEnumerable<NodePreferenceComparerTestCase> Data() {
// 			yield return new(Leader, [Leader, Follower, Follower, ReadOnlyReplica]);
// 			yield return new(Follower, [Follower, Follower, ReadOnlyReplica]);
// 			yield return new(ReadOnlyReplica, [ReadOnlyReplica, PreReadOnlyReplica, ReadOnlyLeaderless]);
// 			yield return new(PreReadOnlyReplica, [PreReadOnlyReplica, ReadOnlyLeaderless]);
// 			yield return new(ReadOnlyLeaderless, [ReadOnlyLeaderless, DiscoverLeader]);
// 		}
// 	}
//
// 	[Test, LeaderTestCases]
// 	public void LeaderTests(NodePreferenceComparerTestCase testCase) {
// 		var actual = testCase.States
// 			.OrderBy(state => state, NodePreferenceComparers.Leader)
// 			.ThenBy(_ => Guid.NewGuid())
//             .FirstOrDefault();
//
// 		actual.ShouldBe(testCase.Expected);
// 	}
//
//     public class FollowerTestCases : TestCaseGenerator<NodePreferenceComparerTestCase> {
// 		protected override IEnumerable<NodePreferenceComparerTestCase> Data() {
// 			yield return new(Follower, [Leader, Follower, Follower, ReadOnlyReplica]);
// 			yield return new(Leader, [Leader, ReadOnlyReplica, ReadOnlyReplica]);
// 			yield return new(ReadOnlyReplica, [ReadOnlyReplica, PreReadOnlyReplica, ReadOnlyLeaderless]);
// 			yield return new(PreReadOnlyReplica, [PreReadOnlyReplica, ReadOnlyLeaderless]);
// 			yield return new(ReadOnlyLeaderless, [ReadOnlyLeaderless, DiscoverLeader]);
// 		}
// 	}
//
//     public record struct NodePreferenceComparerTestCase(ClusterMessages.VNodeState Expected, ClusterMessages.VNodeState[] States);
//
// 	[Test, FollowerTestCases]
// 	public void FollowerTests(NodePreferenceComparerTestCase testCase) {
//         var actual = testCase.States
// 			.OrderBy(state => state, NodePreferenceComparers.Follower)
// 			.ThenBy(_ => Guid.NewGuid())
// 			.FirstOrDefault();
//
// 		actual.ShouldBe(testCase.Expected);
// 	}
//
// 	public class ReadOnlyReplicaTestCases : TestCaseGenerator<NodePreferenceComparerTestCase> {
// 		protected override IEnumerable<NodePreferenceComparerTestCase> Data() {
// 			yield return new(ReadOnlyReplica, [Leader, Follower, Follower, ReadOnlyReplica]);
// 			yield return new(PreReadOnlyReplica, [Leader, Follower, Follower, PreReadOnlyReplica]);
// 			yield return new(ReadOnlyLeaderless, [Leader, Follower, Follower, ReadOnlyLeaderless]);
// 			yield return new(Leader, [Leader, Follower, Follower]);
// 			yield return new(Follower, [DiscoverLeader, Follower, Follower]);
// 		}
// 	}
//
// 	[Test, ReadOnlyReplicaTestCases]
// 	public void ReadOnlyReplicaTests(NodePreferenceComparerTestCase testCase) {
// 		var actual = testCase.States
// 			.OrderBy(state => state, NodePreferenceComparers.ReadOnlyReplica)
// 			.ThenBy(_ => Guid.NewGuid())
//             .FirstOrDefault();
//
// 		actual.ShouldBe(testCase.Expected);
// 	}
// }
