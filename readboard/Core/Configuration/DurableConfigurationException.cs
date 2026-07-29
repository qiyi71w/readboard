using System;
using System.Collections.Generic;
using System.IO;

namespace readboard
{
    internal sealed class DurableConfigurationException : IOException
    {
        public DurableConfigurationException(
            string message,
            Exception primaryFailure,
            Exception recoveryFailure,
            string transactionDirectory)
            : base(
                message + " Transaction directory: " + transactionDirectory,
                CreateInnerException(message, primaryFailure, recoveryFailure))
        {
            PrimaryFailure = primaryFailure;
            RecoveryFailure = recoveryFailure;
            TransactionDirectory = transactionDirectory;
        }

        public Exception PrimaryFailure { get; private set; }
        public Exception RecoveryFailure { get; private set; }
        public string TransactionDirectory { get; private set; }

        private static Exception CreateInnerException(
            string message,
            Exception primaryFailure,
            Exception recoveryFailure)
        {
            List<Exception> failures = new List<Exception>();
            if (primaryFailure != null)
                failures.Add(primaryFailure);
            if (recoveryFailure != null)
                failures.Add(recoveryFailure);
            if (failures.Count == 1)
                return failures[0];
            return new AggregateException(message, failures);
        }
    }
}
