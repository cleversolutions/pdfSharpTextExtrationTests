using System.Collections.Generic;

public class CodeSpaceRange{
    public CodeSpaceRange(){
        Mapping = new Dictionary<int, int>();
    }
    
    public int Low{get;set;}
    public int High{get;set;}
    public int NumberOfBytes{get;set;}
    public Dictionary<int,int> Mapping{get;set;}
}