using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.IO;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Threading;

namespace BlockChain
{
    public partial class Form1 : Form
    {
        static string locIP = "127.0.0.1";
        public int locPort = 65002;
        public int msgBody = 4;

        public List<int> networks = new List<int>();

        public TcpListener server;
        public String nodeName;
        public List<Block> blockChain = new List<Block>();

        public int diffAdjustInterval = 10; 
        public long timeExpected = 200_000;

        #region NetProtocols
        public void Posli(NetworkStream ns, byte[] msg)
        {
            try
            {
                ns.Write(msg, 0, msg.Length);
            }
            catch (Exception e)
            {
                Console.WriteLine("Napaka pri posiljanje: " + e.Message);
            }
        }
        public byte[] Prejemi(NetworkStream ns, int msgSize)
        {
            try
            {
                byte[] buffer = new byte[msgSize];
                int len = ns.Read(buffer, 0, buffer.Length);
                return buffer;
            }
            catch (Exception e)
            {
                Console.WriteLine("Napaka pri prejemu: " + e.Message);
                return null;
            }

        }
        #endregion

        // Block structure
        public class Block
        {
            public int index { get; set; }
            public string data { get; set; }
            public DateTime time { get; set; }
            public byte[] hash { get; set; }
            public byte[] previousHash { get; set; }

            public long nonce { get; set; }
            public int diff { get; set; }
        }

        // Updator for all the other nodes in the network
        public void UpdateNet()
        {
            byte[] dataToSend = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(blockChain));
            byte[] dataSize = BitConverter.GetBytes(dataToSend.Length);
            List<int> removeNets = new List<int>();
            foreach (int c in networks)
            {
                try
                {
                    TcpClient newConn = new TcpClient();
                    newConn.Connect(locIP, c);
                    NetworkStream ns = newConn.GetStream();

                    Posli(ns, dataSize);
                    Posli(ns, dataToSend);
                }
                catch
                {
                    Console.WriteLine("Povezava vec ne obstaja!");
                    removeNets.Add(c);
                }
            }
            foreach (int c in removeNets)
            {
                Console.WriteLine("Odstranjevanje Port: " + c);
                networks.Remove(c);
            }
        }

        // Block validation
        public bool ValidateBlock(Block currentBlock, Block previousBlock)
        {
            //Console.WriteLine("[Validating]" + JsonSerializer.Serialize(currentBlock));
            if (Convert.ToBase64String(currentBlock.previousHash) == Convert.ToBase64String(previousBlock.hash))
            if (Convert.ToBase64String(currentBlock.hash) == Convert.ToBase64String(CalculateHash(currentBlock)))
                    return true;
            //Console.WriteLine("[Validation fail] " + (currentBlock.previousHash == previousBlock.hash) + "|" + (Convert.ToBase64String(currentBlock.hash) == Convert.ToBase64String(CalculateHash(currentBlock))));
            //Console.WriteLine("[Previous block hash] " + Convert.ToBase64String(previousBlock.hash));
            //Console.WriteLine("[Current block hash] " + Convert.ToBase64String(currentBlock.hash));
            //Console.WriteLine("[Calculate hash] " + Convert.ToBase64String(CalculateHash(currentBlock)));
            return false;
        }

        // Cehck if every block in a chain is valid
        public bool ValidateChain(List<Block> chain)
        {
            for(int i = 0; i < chain.Count - 1; i++)
            {
                if(!ValidateBlock(chain.ElementAt(i+1), chain.ElementAt(i)))
                {
                    Console.WriteLine("[Chain Validation Failed]");
                    return false;
                }
            }
            Console.WriteLine("[Chain Validation Succeeded]");
            return true;
        }

        // Hash calculator
        public byte[] CalculateHash(Block currectBlock)
        {
            using (SHA256 sha = new SHA256Managed())
            {
                string strToHash = currectBlock.index +
                    currectBlock.previousHash.ToString() +
                    currectBlock.data +
                    currectBlock.time.ToString() +
                    currectBlock.diff +
                    currectBlock.nonce;
                byte[] byteToHash = Encoding.UTF8.GetBytes(strToHash);

                byte[] rtn = sha.ComputeHash(byteToHash);
                return rtn;
            }
        }

        // Block difficulty calculator
        public int DifficultyOf(byte[] hash)
        {
            string chars = Convert.ToBase64String(hash);
            //for (int i = 0; i < chars.Length; i++)
            //{
            //    Console.WriteLine(chars[i]);
            //}
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] != '0')
                    return i;
            }
            return chars.Length;
        }

        // Delegates for Refreshing
        #region Delegates
        public delegate void delSync(string data);
        public delegate void delLedger(string data);

        public void RefreshSync(string data)
        {
            boxSync.Text += data;
        }
        public void RefreshLedger(string ledger)
        {
            List<Block> displayLeg = JsonSerializer.Deserialize<List<Block>>(ledger);
            blockChain = displayLeg;
            string displayTxt = "";
            foreach (Block b in displayLeg)
            {
                displayTxt +=
                    "\n____________________\n" +
                    "\nIndex: " + b.index +
                    "\nData: " + b.data +
                    "\nHash: " + Convert.ToBase64String(b.hash) +
                    "\nPrevHash: " + Convert.ToBase64String(b.previousHash) +
                    "\nDif: " + b.diff +
                    "\n____________________\n       |";
            }
            boxLedger.Text = displayTxt;
        }

        #endregion

        // Creation of a new Block
        public Block Mine(Block currentBlock, int netDiff)
        {
            delSync sync = new delSync(RefreshSync);
            using (SHA256 sha = new SHA256Managed())
            {
                long nonce = 0;

                Block newBlock = new Block();
                newBlock.index = currentBlock.index + 1;
                newBlock.previousHash = currentBlock.hash;
                newBlock.data = "Block #" + newBlock.index + " Node:" + nodeName;
                newBlock.time = DateTime.Now;

                Block lastBlock = blockChain.ElementAt(blockChain.Count - 1);
                Block lastAdjustblock = (blockChain.Count > diffAdjustInterval ? blockChain.ElementAt(blockChain.Count - diffAdjustInterval) : blockChain.ElementAt(0));
                int diff = lastBlock.diff;
                long timeTaken = lastBlock.time.Ticks - lastAdjustblock.time.Ticks;
                Console.WriteLine("Time taken: " + timeTaken);
                if(timeTaken > 0)
                {
                    if (timeTaken < (timeExpected / 2))
                    {
                        diff = diff + 1;
                    }
                    else if (timeTaken > (timeExpected * 2))
                    {
                        diff = diff - 1;
                    }
                }
                diff = (diff < 0 ? 0 : diff);
                Console.WriteLine("[Node " + nodeName + "]Mining for dif:" + diff);
                //if (hash ustreza diff) then return Block(index, previousHash, timestamp, data, diff, nonce)​
                string curBlockStr = JsonSerializer.Serialize(currentBlock);
                while (true)
                {
                    lastBlock = blockChain.ElementAt(blockChain.Count - 1);

                    string strToHash = newBlock.index +
                        newBlock.previousHash.ToString() +
                        newBlock.data +
                        newBlock.time.ToString() +
                        diff +
                        nonce;

                    byte[] byteToHash = Encoding.UTF8.GetBytes(strToHash);

                    byte[] hash = sha.ComputeHash(byteToHash);
                    //Console.WriteLine("[Node:" + nodeName + "] New hash: " + Convert.ToBase64String(hash));

                    if (DifficultyOf(hash) == diff)
                    {
                        newBlock.hash = hash;
                        newBlock.diff = diff;
                        newBlock.nonce = nonce;

                        Console.WriteLine("\nDifficulty is valid for: " + Convert.ToBase64String(hash));
                        boxSync.BeginInvoke(sync, "\n[USTREZNO!] " + Convert.ToBase64String(hash) + "\nDiff: " + DifficultyOf(hash));

                        return newBlock;
                    }
                    nonce++;
                    if(curBlockStr != JsonSerializer.Serialize(lastBlock))
                    {
                        Console.WriteLine("Veriga se spremenila!");
                        newBlock.hash = hash;
                        newBlock.diff = diff;
                        newBlock.nonce = nonce;
                        return newBlock;
                    }
                }
            }
        }

        // Compare two block chains blockchains
        public List<Block> CompareChains(List<Block> c1, List<Block> c2)
        {
            long diff1=0, diff2=0;
            foreach(Block b in c1)
            {
                diff1 += b.diff;
            }
            foreach(Block b in c2)
            {
                diff2 += b.diff;
            }
            return (Math.Pow(2, diff1) > Math.Pow(2, diff2) ? c1 : c2);
        }

        // Server listener. Listens for new ledgers from other nodes. 
        public void Server(TcpListener server)
        {
            delLedger leg = new delLedger(RefreshLedger);
            while (true)
            {
                try
                {
                    TcpClient noviClient = server.AcceptTcpClient();
                    Console.WriteLine("Accepting New Client");
                    NetworkStream ns = noviClient.GetStream();

                    byte[] received = Prejemi(ns, 4);
                    Console.WriteLine("Successfully gotten new message.");
                    Console.WriteLine(BitConverter.ToInt32(received, 0));

                    msgBody = BitConverter.ToInt32(received, 0);
                    received = Prejemi(ns, msgBody);
                    Console.WriteLine("Successfully gotten new message.");
                    Console.WriteLine(Encoding.UTF8.GetString(received));


                    List<Block> newChain = JsonSerializer.Deserialize<List<Block>>(Encoding.UTF8.GetString(received));

                    if (ValidateChain(newChain))
                    {
                        List<Block> bigger = CompareChains(blockChain, newChain);
                        boxLedger.BeginInvoke(leg, JsonSerializer.Serialize(bigger));
                    }

                }
                catch
                {
                    Console.WriteLine("Error In Accepting New Client");
                }
            }
        }

        // Loop that mines
        public void MiningCycle()
        {
            //int blocksMined = 0;
            while (true)
            {
                Block newBlock = Mine(blockChain.ElementAt(blockChain.Count - 1), 0);
                if (ValidateBlock(newBlock, blockChain.ElementAt(blockChain.Count - 1)))
                {
                    blockChain.Add(newBlock);
                    Console.WriteLine("Successfully added to chain");
                    //blocksMined++;
                }
            }
        }
        // refresh functions
        #region Refreshing
        DispatcherTimer timer;
        DispatcherTimer timerLedger;

        public void UpdateTimer(object sender, EventArgs e)
        {
            UpdateNet();
        }

        public void UpdateLedgerTimer(object sender, EventArgs e)
        {
            RefreshLedger(JsonSerializer.Serialize(blockChain));
        }

        // refresh times
        public int refreshTime = 20; 
        public int refreshTimeLedger = 1;
        #endregion
        public Form1()
        {
            InitializeComponent();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(refreshTime);
            timer.Tick += UpdateTimer;
            timer.Start();

            timerLedger = new DispatcherTimer();
            timerLedger.Interval = TimeSpan.FromSeconds(refreshTimeLedger);
            timerLedger.Tick += UpdateLedgerTimer;
            timerLedger.Start();

        }

        #region Buttons
        // Button to activate the node
        private void btnStart_Click(object sender, EventArgs e)
        {
            btnStart.Enabled = false;
            nodeName = txtName.Text;
            txtName.Enabled = false;
            btnConnect.Enabled = true;
            btnMine.Enabled = true;
            txtPort.Enabled = true;

            bool failed = true;
            while (failed)
            {
                try
                {
                    server = new TcpListener(IPAddress.Parse(locIP), locPort);
                    server.Start();
                    failed = false;
                    labelStatus.Text = "(Online) Port: " + locPort;
                    Task.Run(() => Server(server));
                    blockChain = new List<Block>();
                }
                catch
                {
                    locPort++;
                }
            }
        }

        // Button for adding a port to connect to
        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                if (txtPort.Text == null || 
                    txtPort.Text == "" || 
                    int.Parse(txtPort.Text) == locPort || 
                    networks.Contains(int.Parse(txtPort.Text)))
                    return;
                int connectTo = int.Parse(txtPort.Text);
                networks.Add(connectTo);
                boxSync.Text += "[Connection] Port:" + locPort;
            }
            catch
            {
                boxSync.Text += "Node na port (" + txtPort.Text + ") ne obstaja! \n";
                txtPort.Text = "";
            }
        }

        // Button for starting the mine
        private void btnMine_Click(object sender, EventArgs e)
        {
            try
            {
                Block newBlock = new Block();
                using (SHA256 sha = new SHA256Managed())
                {
                    if (blockChain.Count == 0)
                        {
                            newBlock.index = 0;
                            newBlock.previousHash = new byte[0];
                            newBlock.data = "Block #" + newBlock.index + " Node: " + nodeName;
                            newBlock.time = DateTime.Now;

                            string strToHash = newBlock.index + newBlock.previousHash.ToString() + newBlock.data + newBlock.time.ToString();
                            byte[] byteToHash = Encoding.UTF8.GetBytes(strToHash);

                            byte[] hash = sha.ComputeHash(byteToHash);

                            //Console.WriteLine("Successfully created block with hash:" + Convert.ToBase64String(hash));

                            newBlock.hash = hash;
                            newBlock.diff = DifficultyOf(hash);
                            newBlock.nonce = 0;

                            blockChain.Add(newBlock);
                            Console.WriteLine("Successfully added to chain");
                        }
                    Task.Run(() => MiningCycle());

                    btnMine.Enabled = false;
                }

            }
            catch
            {
                Console.WriteLine("Failed to send a message.");

            }
        }
        #endregion
    }
}
