before going on I want to update the roadmap: I want to implement a new Spice.IntegrationTests to test the testpo files. 

I do not want to add the test files and ephemeris files to git, they have to be dowloaded (if not already present) to a local directory (still in the project directory) and stored locally for subsequent test execution.

the testpo files are available online with the following pattern:
https://ssd.jpl.nasa.gov/ftp/eph/planets/ascii/de{ephNumber}/testpo.{ephNumber}
with ephNumber being 102,200,202,403,405,406,410,413,414,418,421,422,423,424,430,430t,431,432,432t,433,434,435,436,436t,438,438t,440,440t,441

the ephemeris files are available as 
https://ssd.jpl.nasa.gov/ftp/eph/planets/bsp/de{ephNumber}.bsp

bsp files must be dowloaded only if the size is under 150 MB, bigger files must be tested only optionally the file sizes are listed as follows:
de102.bsp	2011-03-24 01:30	228.1M	File
de200.bsp	2011-03-18 01:03	54.2M	File
de202.bsp	2011-03-28 19:07	14.3M	File
de403.bsp	2000-10-10 00:46	62.3M	File
de405.bsp	2000-10-10 00:46	62.4M	File
de405_1960_2020.bsp	2002-02-26 20:14	6.2M	File
de406.bsp	2000-10-10 00:46	286.9M	File
de410.bsp	2011-03-18 01:04	12.5M	File
de413.bsp	2011-03-18 01:04	15.6M	File
de414.bsp	2011-03-18 01:05	62.4M	File
de418.bsp	2011-03-18 01:05	15.7M	File
de421.bsp	2008-02-12 20:41	16.0M	File
de422.bsp	2012-05-11 19:56	622.7M	File
de422_1850_2050.bsp	2009-09-29 18:59	20.8M	File
de423.bsp	2010-02-12 18:42	41.5M	File
de424.bsp	2012-02-06 18:33	62.3M	File
de424s.bsp	2013-02-01 21:53	6.2M	File
de425.bsp	2014-04-02 17:59	62.3M	File
de430_1850-2150.bsp	2014-06-30 22:27	31.2M	File
de430_plus_MarsPC.bsp	2014-06-30 22:33	132.3M	File
de430t.bsp	2014-06-30 22:17	127.7M	File
de431t.bsp	2014-06-30 22:58	3.44G	File
de432t.bsp	2014-06-30 22:11	127.7M	File
de433.bsp	2018-04-27 20:02	389.3M	File
de433_plus_MarsPC.bsp	2015-02-11 23:40	129.4M	File
de433t.bsp	2018-04-27 20:02	145.6M	File
de434.bsp	2016-02-20 01:19	114.2M	File
de434s.bsp	2016-02-20 01:19	20.8M	File
de434t.bsp	2016-02-20 01:19	145.6M	File
de435.bsp	2016-03-05 00:54	114.2M	File
de435s.bsp	2016-03-05 00:54	20.8M	File
de435t.bsp	2016-03-05 00:54	145.6M	File
de436.bsp	2016-11-02 00:45	114.2M	File
de436s.bsp	2016-11-02 22:05	20.8M	File
de436t.bsp	2016-11-02 00:45	145.6M	File
de438.bsp	2018-03-30 23:44	114.2M	File
de438_plus_MarsPC.bsp	2018-03-30 23:44	132.3M	File
de438s.bsp	2018-03-30 23:44	20.8M	File
de438t.bsp	2018-04-03 22:55	145.6M	File
de440.bsp	2020-12-22 00:56	114.3M	File
de440s.bsp	2020-12-22 00:56	31.2M	File
de440s_plus_MarsPC.bsp	2020-12-22 00:58	66.2M	File
de440t.bsp	2020-12-22 00:56	145.7M	File
de441.bsp	2020-12-22 00:57	3.08G	File

The example of fist lines of a testpo file is given below, my guess is that for each line after EOT, a query date,julian day,target,center,coodinate index is given and the expected result is the last column. coordinates are indexed as x,y,z,vx,vy,vz

START EXAMLE


 DE-0440LE-0440        1549-DEC-21 00:00  JD   2287184.50 to   2650-JAN-25 00:00  JD   2688976.50


KSIZE= 2036 
 
de#  -- date -- -- jed -- t# c# x# -- coordinate ---
EOT
440  1550.01.01 2287195.5 14  0  1        0.00001572741171979760
440  1550.02.01 2287226.5  9 13  5        0.01555051938366733104
440  1550.03.01 2287254.5 14  0  1        0.00001119602761319100
440  1550.04.01 2287285.5  7 13  6        0.00451250557463989121
440  1550.05.01 2287315.5 12  4  2        0.54646766849518868536
440  1550.06.01 2287346.5  2  1  5       -0.01555956312438864890
440  1550.07.01 2287376.5  7  1  5        0.01830315594878903149
440  1550.08.01 2287407.5  9  7  4        0.00099978582122729276
440  1550.09.01 2287438.5  4  3  6       -0.00219499755116558514

END EXAMPLE