﻿namespace Tvl.VisualStudio.Language.Parsing
{
    using Antlr.Runtime;

    public interface ITokenSourceWithState<T> : ITokenSource
        where T : struct
    {
        ICharStream CharStream
        {
            get;
        }

        T GetCurrentState();
    }
}
