using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace GameLogic.Tests
{
    public sealed class MatchStoreTests
    {
        [UnityTest] public IEnumerator FailedWritePreservesLastCommittedRecordAndRefundIsOnce() => FailedWriteAsync().ToCoroutine();
        private async UniTask FailedWriteAsync()
        {
            var directory=Path.Combine(Path.GetTempPath(),"FreshGuard-match-fail-"+Guid.NewGuid().ToString("N"));
            var path=Path.Combine(directory,"record.json");
            try
            {
                var store=new MatchPlayerStore(path);
                var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
                var record=new MatchPlayerRecord();record.Capture(state);await store.SaveAsync(record);
                record.BeginEntry(state,"failed-entry",DateTimeOffset.UtcNow);
                Directory.CreateDirectory(path+".tmp");
                var failed=false;
                try {await store.SaveAsync(record);} catch(IOException){failed=true;} catch(UnauthorizedAccessException){failed=true;}
                Assert.That(failed,Is.True,"临时文件不可写时必须报告失败");
                Assert.That((await store.LoadAsync()).Energy,Is.EqualTo(30),"失败写入不得替换已提交记录");
                Assert.That(record.RefundFailure(state,"failed-entry",DateTimeOffset.UtcNow),Is.True);
                Assert.That(record.RefundFailure(state,"failed-entry",DateTimeOffset.UtcNow),Is.False);
                Directory.Delete(path+".tmp");await store.SaveAsync(record);
                var restored=await store.LoadAsync();Assert.That(restored.Energy,Is.EqualTo(30));
                Assert.That(restored.RefundMatching(state,DateTimeOffset.UtcNow),Is.False);
            }
            finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [UnityTest] public IEnumerator RestartReloadRefundSaveAndReloadAgainIsExactlyOnce() => RunAsync().ToCoroutine();
        private async UniTask RunAsync()
        {
            var directory=Path.Combine(Path.GetTempPath(),"FreshGuard-match-"+Guid.NewGuid().ToString("N"));
            var path=Path.Combine(directory,"record.json");
            try
            {
                var store=new MatchPlayerStore(path);
                var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
                var record=new MatchPlayerRecord();record.BeginEntry(state,"entry",DateTimeOffset.UtcNow);
                await store.SaveAsync(record);
                var restarted=new MatchPlayerStore(path);var loaded=await restarted.LoadAsync();
                var restored=loaded.Restore(30,TimeSpan.FromMinutes(5),DateTimeOffset.UtcNow);
                Assert.That(restored.Energy,Is.EqualTo(25));Assert.That(loaded.RefundMatching(restored,DateTimeOffset.UtcNow),Is.True);
                await restarted.SaveAsync(loaded);
                var again=await new MatchPlayerStore(path).LoadAsync();
                var second=again.Restore(30,TimeSpan.FromMinutes(5),DateTimeOffset.UtcNow);
                Assert.That(second.Energy,Is.EqualTo(30));Assert.That(again.RefundMatching(second,DateTimeOffset.UtcNow),Is.False);
                Assert.That(File.Exists(path+".tmp"),Is.False);
                again.BeginEntry(second,"entry-2",DateTimeOffset.UtcNow);again.CommitSuccess("entry-2");again.Capture(second);
                await store.SaveAsync(again);
                var success=await restarted.LoadAsync();var successState=success.Restore(30,TimeSpan.FromMinutes(5),DateTimeOffset.UtcNow);
                Assert.That(success.RefundMatching(successState,DateTimeOffset.UtcNow),Is.False);Assert.That(successState.Energy,Is.EqualTo(25));
            }
            finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        }
    }
}
