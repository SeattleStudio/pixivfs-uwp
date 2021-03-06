﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.UI.Xaml.Data;

namespace PixivFSUWP.Data
{
    public class CommentsCollection : ObservableCollection<ViewModels.CommentViewModel>, ISupportIncrementalLoading
    {
        string nexturl = "begin";
        bool _busy = false;
        bool _emergencyStop = false;
        EventWaitHandle pause = new ManualResetEvent(true);
        readonly string illustid;

        public CommentsCollection(string IllustID) => illustid = IllustID;

        public bool HasMoreItems
        {
            get => nexturl != "";
        }

        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            if (_busy)
                throw new InvalidOperationException("Only one operation in flight at a time");
            _busy = true;
            return AsyncInfo.Run((c) => LoadMoreItemsAsync(c, count));
        }

        public void StopLoading()
        {
            _emergencyStop = true;
            if (_busy)
            {
                ResumeLoading();
            }
            else
            {
                Clear();
                GC.Collect();
            }
        }

        public void PauseLoading()
        {
            pause.Reset();
        }

        public void ResumeLoading()
        {
            pause.Set();
        }

        protected async Task<LoadMoreItemsResult> LoadMoreItemsAsync(CancellationToken c, uint count)
        {
            try
            {
                if (!HasMoreItems) return new LoadMoreItemsResult() { Count = 0 };
                LoadMoreItemsResult toret = new LoadMoreItemsResult() { Count = 0 };
                JsonObject commentres = null;
                try
                {
                    if (nexturl == "begin")
                        commentres = await new PixivCS
                            .PixivAppAPI(OverAll.GlobalBaseAPI)
                            .IllustComments(illustid, IncludeTotalComments: true);
                    else
                    {
                        Uri next = new Uri(nexturl);
                        string getparam(string param) => HttpUtility.ParseQueryString(next.Query).Get(param);
                        commentres = await new PixivCS
                            .PixivAppAPI(OverAll.GlobalBaseAPI)
                            .IllustComments(illustid, getparam("offset"), bool.Parse(getparam("include_total_comments")));
                    }
                }
                catch
                {
                    return toret;
                }
                nexturl = commentres["next_url"].TryGetString();
                foreach (var recillust in commentres["comments"].GetArray())
                {
                    await Task.Run(() => pause.WaitOne());
                    if (_emergencyStop)
                    {
                        nexturl = "";
                        Clear();
                        return new LoadMoreItemsResult() { Count = 0 };
                    }
                    Data.IllustCommentItem recommendi = Data.IllustCommentItem.FromJsonValue(recillust.GetObject());
                    var recommendmodel = ViewModels.CommentViewModel.FromItem(recommendi);
                    //await recommendmodel.LoadAvatarAsync();
                    Add(recommendmodel);
                    toret.Count++;
                }
                return toret;
            }
            finally
            {
                _busy = false;
                if (_emergencyStop)
                {
                    nexturl = "";
                    Clear();
                    GC.Collect();
                }
            }
        }
    }
}
