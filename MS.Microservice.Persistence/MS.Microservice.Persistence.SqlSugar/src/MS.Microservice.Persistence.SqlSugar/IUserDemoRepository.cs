using MS.Microservice.Core.Domain.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Domain.SqlSugar.Repository
{
	public interface IUserDemoRepository: IRepositoryBase<UserDemo>
	{
	}

	public class UserDemo { }
}
