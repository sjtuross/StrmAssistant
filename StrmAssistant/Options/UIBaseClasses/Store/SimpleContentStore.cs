namespace StrmAssistant.Options.UIBaseClasses.Store
{
    using System;

    using Emby.Web.GenericEdit;

    public class SimpleContentStore<TOptionType>
        where TOptionType : EditableOptionsBase, new()
    {
        private readonly object lockObj = new object();

        private TOptionType options;

        public SimpleContentStore()
        {
        }

        public virtual TOptionType GetOptions()
        {
            lock (this.lockObj)
            {
                if (this.options == null)
                {
                    this.options = new TOptionType();
                }

                return this.options;
            }
        }

        public virtual void SetOptions(TOptionType newOptions)
        {
            if (newOptions == null)
            {
                throw new ArgumentNullException(nameof(newOptions));
            }

            lock (this.lockObj)
            {
                this.options = newOptions;
            }
        }
    }
}
