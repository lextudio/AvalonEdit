using System.Threading.Tasks;

namespace ICSharpCode.AvalonEdit.DevFlowIntegrationTests
{
	public abstract class IntegrationTestBase
	{
		protected static async Task<DevFlowClient> TryConnectAsync()
		{
			var port = DevFlowClient.ResolvePortOrNull() ?? 9224;
			var client = new DevFlowClient(port);
			if (await client.IsReachableAsync())
				return client;

			client.Dispose();
			if (DevFlowClient.ResolvePortOrNull().HasValue)
				throw new System.InvalidOperationException($"No DevFlow agent reachable on port {port}.");

			return null;
		}
	}
}
