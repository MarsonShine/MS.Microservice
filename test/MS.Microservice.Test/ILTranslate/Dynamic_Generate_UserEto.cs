using System.Collections.Generic;

namespace MS.Microservice.Test.ILTranslate {
    public class Dynamic_Generate_UserEto {
        private readonly Dictionary<string, int> _map;
        private readonly int _number;
        public Dynamic_Generate_UserEto(
            Dictionary<string, int> map,
            int number) {
            _map = map;
            _number = number;
            //string name = "marsonshine";
        }
    }
}