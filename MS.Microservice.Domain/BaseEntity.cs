using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Microservice.Domain
{
    public abstract class BaseEntity : IAggregateRoot
    {
        private int id;
        public virtual int ID
        {
            get
            {
                return id;
            }
        }
        private bool isDelete;
        public virtual bool IsDelete
        {
            get { return isDelete; }
            protected set { isDelete = value; }
        }
        //逻辑删除
        protected void Delete()
        {
            this.IsDelete = true;
        }

        protected void SetID(int id)
        {
            this.id = id;
        }
        private DateTime creationTime;
        public virtual DateTime CreationTime
        {
            get { return creationTime; }
            protected set { creationTime = value; }
        }
    }
}
