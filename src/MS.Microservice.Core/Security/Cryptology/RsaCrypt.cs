using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology
{
    public partial class CryptologyHelper
    {
        /// <summary>
        /// rsa，默认用的pkcs8
        /// </summary>
        public static class RsaCrypt
        {
            public static string Encrypt(string resData, string publicKey,Encoding encoding)
            {
                byte[] dataToEncrypt = encoding.GetBytes(resData);
                string result = Encrypt(dataToEncrypt, publicKey);
                return result;
            }

            private static string Encrypt(byte[] data, string publicKey)
            {
                RSACryptoServiceProvider? rsa = DecodePemPublicKey(publicKey) ?? throw new ArgumentNullException(nameof(publicKey));
                byte[] result = rsa.Encrypt(data, false);

                return Convert.ToBase64String(result);
            }

            private static RSACryptoServiceProvider? DecodePemPublicKey(string pemstr)
            {
                byte[] pkcs8publickkey;
                pkcs8publickkey = Convert.FromBase64String(pemstr);
                if (pkcs8publickkey != null)
                {
                    RSACryptoServiceProvider? rsa = DecodeRSAPublicKey(pkcs8publickkey);
                    return rsa;
                }
                throw new ArgumentException("public key is not valid");
            }

            private static RSACryptoServiceProvider? DecodeRSAPublicKey(byte[] publickey)
            {
                // encoded OID sequence for  PKCS #1 rsaEncryption szOID_RSA_RSA = "1.2.840.113549.1.1.1"  
                byte[] SeqOID = { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 };
                // ---------  Set up stream to read the asn.1 encoded SubjectPublicKeyInfo blob  ------  
                using var mem = new MemoryStream(publickey);
                using var binr = new BinaryReader(mem);
                //wrap Memory Stream with BinaryReader for easy reading  
                byte bt = 0;
                ushort twobytes = 0;
                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130) //data read as little endian order (actual data order for Sequence is 30 81)  
                    binr.ReadByte();    //advance 1 byte  
                else if (twobytes == 0x8230)
                    binr.ReadInt16();   //advance 2 bytes  
                else
                    return null;

                byte[] seq = binr.ReadBytes(15);
                if (!CompareBytearrays(seq, SeqOID))    //make sure Sequence for OID is correct  
                    return null;

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8103) //data read as little endian order (actual data order for Bit String is 03 81)  
                    binr.ReadByte();    //advance 1 byte  
                else if (twobytes == 0x8203)
                    binr.ReadInt16();   //advance 2 bytes  
                else
                    return null;

                bt = binr.ReadByte();
                if (bt != 0x00)     //expect null byte next  
                    return null;

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130) //data read as little endian order (actual data order for Sequence is 30 81)  
                    binr.ReadByte();    //advance 1 byte  
                else if (twobytes == 0x8230)
                    binr.ReadInt16();   //advance 2 bytes  
                else
                    return null;

                twobytes = binr.ReadUInt16();
                byte lowbyte = 0x00;
                byte highbyte = 0x00;

                if (twobytes == 0x8102) //data read as little endian order (actual data order for Integer is 02 81)  
                    lowbyte = binr.ReadByte();  // read next bytes which is bytes in modulus  
                else if (twobytes == 0x8202)
                {
                    highbyte = binr.ReadByte(); //advance 2 bytes  
                    lowbyte = binr.ReadByte();
                }
                else
                    return null;
                byte[] modint = { lowbyte, highbyte, 0x00, 0x00 };   //reverse byte order since asn.1 key uses big endian order  
                int modsize = BitConverter.ToInt32(modint, 0);

                byte firstbyte = binr.ReadByte();
                binr.BaseStream.Seek(-1, SeekOrigin.Current);

                if (firstbyte == 0x00)
                {   //if first byte (highest order) of modulus is zero, don't include it  
                    binr.ReadByte();    //skip this null byte  
                    modsize -= 1;   //reduce modulus buffer size by 1  
                }

                byte[] modulus = binr.ReadBytes(modsize);   //read the modulus bytes  

                if (binr.ReadByte() != 0x02)            //expect an Integer for the exponent data  
                    return null;
                int expbytes = (int)binr.ReadByte();        // should only need one byte for actual exponent data (for all useful values)  
                byte[] exponent = binr.ReadBytes(expbytes);

                // ------- create RSACryptoServiceProvider instance and initialize with public key -----  
                var RSA = new RSACryptoServiceProvider();
                var RSAKeyInfo = new RSAParameters
                {
                    Modulus = modulus,
                    Exponent = exponent
                };
                RSA.ImportParameters(RSAKeyInfo);
                return RSA;
            }

            private static bool CompareBytearrays(byte[] a, byte[] b)
            {
                if (a.Length != b.Length)
                    return false;
                int i = 0;
                foreach (byte c in a)
                {
                    if (c != b[i])
                        return false;
                    i++;
                }
                return true;
            }

            public static string Decrypt(string rsaData, string privateKey, Encoding encoding)
            {
                byte[] dataToDecrypt = Convert.FromBase64String(rsaData);
                string result = "";
                for (int j = 0; j < dataToDecrypt.Length / 128; j++)
                {
                    byte[] buf = new byte[128];
                    for (int i = 0; i < 128; i++)
                    {

                        buf[i] = dataToDecrypt[i + 128 * j];
                    }
                    result += Decrypt(buf, privateKey, encoding);
                }
                return result;
            }

            private static string Decrypt(byte[] data, string privateKey, Encoding encoding)
            {
                RSACryptoServiceProvider rsa = DecodePemPrivateKey(privateKey) ?? throw new ArgumentNullException(nameof(privateKey));
                byte[] source = rsa.Decrypt(data, false);
                char[] asciiChars = new char[encoding.GetCharCount(source, 0, source.Length)];
                encoding.GetChars(source, 0, source.Length, asciiChars, 0);
                string result = new(asciiChars);
                return result;
            }
            private static RSACryptoServiceProvider? DecodePemPrivateKey(string pemstr)
            {
                byte[] pkcs8privatekey;
                pkcs8privatekey = Convert.FromBase64String(pemstr);
                if (pkcs8privatekey != null)
                {
                    RSACryptoServiceProvider? rsa = DecodePrivateKeyInfo(pkcs8privatekey);
                    return rsa;
                }
                else
                    return null;
            }
            private static RSACryptoServiceProvider? DecodePrivateKeyInfo(byte[] pkcs8)
            {
                byte[] SeqOID = { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 };
                byte[] seq = new byte[15];

                using var mem = new MemoryStream(pkcs8);
                using var binr = new BinaryReader(mem);
                int lenstream = (int)mem.Length;
                byte bt = 0;
                ushort twobytes = 0;
                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130)    //data read as little endian order (actual data order for Sequence is 30 81)  
                    binr.ReadByte();    //advance 1 byte  
                else if (twobytes == 0x8230)
                    binr.ReadInt16();    //advance 2 bytes  
                else
                    return null;

                bt = binr.ReadByte();
                if (bt != 0x02)
                    return null;

                twobytes = binr.ReadUInt16();

                if (twobytes != 0x0001)
                    return null;

                seq = binr.ReadBytes(15);        //read the Sequence OID  
                if (!CompareBytearrays(seq, SeqOID))    //make sure Sequence for OID is correct  
                    return null;

                bt = binr.ReadByte();
                if (bt != 0x04)    //expect an Octet string  
                    return null;

                bt = binr.ReadByte();        //read next byte, or next 2 bytes is  0x81 or 0x82; otherwise bt is the byte count  
                if (bt == 0x81)
                    binr.ReadByte();
                else
                    if (bt == 0x82)
                    binr.ReadUInt16();
                //------ at this stage, the remaining sequence should be the RSA private key  

                byte[] rsaprivkey = binr.ReadBytes((int)(lenstream - mem.Position));
                RSACryptoServiceProvider? rsacsp = DecodeRSAPrivateKey(rsaprivkey);
                return rsacsp;
            }
            private static RSACryptoServiceProvider? DecodeRSAPrivateKey(byte[] privkey)
            {
                byte[] MODULUS, E, D, P, Q, DP, DQ, IQ;

                // ---------  Set up stream to decode the asn.1 encoded RSA private key  ------  
                using var mem = new MemoryStream(privkey);
                using var binr = new BinaryReader(mem);    //wrap Memory Stream with BinaryReader for easy reading  
                byte bt = 0;
                ushort twobytes = 0;
                int elems = 0;
                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130)    //data read as little endian order (actual data order for Sequence is 30 81)  
                    binr.ReadByte();    //advance 1 byte  
                else if (twobytes == 0x8230)
                    binr.ReadInt16();    //advance 2 bytes  
                else
                    return null;

                twobytes = binr.ReadUInt16();
                if (twobytes != 0x0102)    //version number  
                    return null;
                bt = binr.ReadByte();
                if (bt != 0x00)
                    return null;


                //------  all private key components are Integer sequences ----  
                elems = GetIntegerSize(binr);
                MODULUS = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                E = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                D = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                P = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                Q = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                DP = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                DQ = binr.ReadBytes(elems);

                elems = GetIntegerSize(binr);
                IQ = binr.ReadBytes(elems);

                // ------- create RSACryptoServiceProvider instance and initialize with public key -----  
                RSACryptoServiceProvider RSA = new();
                RSAParameters RSAparams = new();
                RSAparams.Modulus = MODULUS;
                RSAparams.Exponent = E;
                RSAparams.D = D;
                RSAparams.P = P;
                RSAparams.Q = Q;
                RSAparams.DP = DP;
                RSAparams.DQ = DQ;
                RSAparams.InverseQ = IQ;
                RSA.ImportParameters(RSAparams);
                return RSA;
            }
            private static int GetIntegerSize(BinaryReader binr)
            {
                byte bt = binr.ReadByte();
                if (bt != 0x02)        //expect integer  
                    return 0;
                bt = binr.ReadByte();

                int count;
                if (bt == 0x81)
                    count = binr.ReadByte();    // data size in next byte  
                else
                    if (bt == 0x82)
                {
                    byte highbyte = binr.ReadByte();
                    byte lowbyte = binr.ReadByte();
                    byte[] modint = { lowbyte, highbyte, 0x00, 0x00 };
                    count = BitConverter.ToInt32(modint, 0);
                }
                else
                {
                    count = bt;        // we already have the data size  
                }



                while (binr.ReadByte() == 0x00)
                {    //remove high order zeros in data  
                    count -= 1;
                }
                binr.BaseStream.Seek(-1, SeekOrigin.Current);        //last ReadByte wasn't a removed zero, so back up a byte  
                return count;
            }
        }
    }

}
