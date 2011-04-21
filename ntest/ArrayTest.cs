using System;
using System.IO;
using CookComputing.XmlRpc;
using NUnit.Framework;

namespace ntest
{
  class ArrayTest
  {
    string expectedJagged =
@"<value>
  <array>
    <data>
      <value>
        <array>
          <data />
        </array>
      </value>
      <value>
        <array>
          <data>
            <value>
              <i4>1</i4>
            </value>
          </data>
        </array>
      </value>
      <value>
        <array>
          <data>
            <value>
              <i4>2</i4>
            </value>
            <value>
              <i4>3</i4>
            </value>
          </data>
        </array>
      </value>
    </data>
  </array>
</value>";

    string expectedMultiDim =
@"<value>
  <array>
    <data>
      <value>
        <array>
          <data>
            <value>
              <i4>1</i4>
            </value>
            <value>
              <i4>2</i4>
            </value>
          </data>
        </array>
      </value>
      <value>
        <array>
          <data>
            <value>
              <i4>3</i4>
            </value>
            <value>
              <i4>4</i4>
            </value>
          </data>
        </array>
      </value>
      <value>
        <array>
          <data>
            <value>
              <i4>5</i4>
            </value>
            <value>
              <i4>6</i4>
            </value>
          </data>
        </array>
      </value>
    </data>
  </array>
</value>";

    [Test]
    public void SerializeJagged()
    {
      var jagged = new int[][] 
      {
        new int[] {},
        new int[] {1},
        new int[] {2, 3}
      };
      string xml = Utils.SerializeValue(jagged, true);
      Assert.AreEqual(expectedJagged, xml);
    }

    [Test]
    public void DeserializeJagged()
    {
      object retVal = Utils.ParseValue(expectedJagged, typeof(int[][]));
      Assert.IsInstanceOf<int[][]>(retVal);
      int[][] ret = (int[][])retVal;
      Assert.IsTrue(ret[0].Length == 0);
      Assert.IsTrue(ret[1].Length == 1);
      Assert.IsTrue(ret[2].Length == 2);
      Assert.AreEqual(1, ret[1][0]);
      Assert.AreEqual(2, ret[2][0]);
      Assert.AreEqual(3, ret[2][1]);
    }

    [Test]
    public void SerializeMultiDim()
    {
      int[,] multiDim = new int[3, 2] { {1, 2}, {3, 4}, {5, 6} };
      string xml = Utils.SerializeValue(multiDim, true);
      Assert.AreEqual(expectedMultiDim, xml);
    }

    [Test]
    public void DeserializeMultiDim()
    {
      object retVal = Utils.ParseValue(expectedMultiDim, typeof(int[,]));
      Assert.IsInstanceOf<int[,]>(retVal);
      int[,] ret = (int[,])retVal;
      Assert.AreEqual(1, ret[0, 0]);
      Assert.AreEqual(2, ret[0, 1]);
      Assert.AreEqual(3, ret[1, 0]);
      Assert.AreEqual(4, ret[1, 1]);
      Assert.AreEqual(5, ret[2, 0]);
      Assert.AreEqual(6, ret[2, 1]);
    }
  }
}