// Copyright (c) Microsoft Corporation. All Rights Reserved.
// Licensed under the MIT License.

using Microsoft.Datasync.Client.Offline.Queue;
using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Microsoft.Datasync.Client.Offline
{
    /// <summary>
    /// A parallel async queue runner that runs the table operations.
    /// </summary>
    internal class QueueHandler
    {
        private ActionBlock<TableOperation> _jobs;
        private readonly int _maxThreads;
        private readonly Func<TableOperation,Task> _jobRunner;

        /// <summary>
        /// Creates a new <see cref="QueueHandler"/>.
        /// </summary>
        /// <param name="maxThreads">The maximum number of threads to run.</param>
        /// <param name="jobRunner">The job runner method.</param>
        public QueueHandler(int maxThreads, Func<TableOperation, Task> jobRunner)
        {
            _maxThreads = maxThreads;
            _jobRunner = jobRunner;

            var options = new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = maxThreads
            };

            _jobs = new ActionBlock<TableOperation>(jobRunner, options);
        }

        public void Restart()
        {
            _jobs = new ActionBlock<TableOperation>(_jobRunner, new ExecutionDataflowBlockOptions {
                MaxDegreeOfParallelism = _maxThreads
            });
        }

        /// <summary>
        /// Enqueue a new job.
        /// </summary>
        /// <param name="operation">The operation to be enqueued.</param>
        public void Enqueue(TableOperation operation)
        {
            _jobs.Post(operation);
        }

        /// <summary>
        /// Wait until all jobs have been completed.
        /// </summary>
        public Task WhenComplete()
        {
            _jobs.Complete();
            return _jobs.Completion;
        }

        /// <summary>
        /// The number of items still to be posted.
        /// </summary>
        public int Count { get => _jobs.InputCount; }
    }
}
