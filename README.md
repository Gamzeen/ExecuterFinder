Kodu localinize indirin. Neo4j kütüphanelerini indirdiğinizden emin olun.
Program.cs dosyasında Main içerisinde rootFolder'ınızı extract etmek istediğiniz dosyanın yolunu verin. 

var neo4jService = new Neo4jService("bolt://localhost:7687", "neo4j", "password");
satırındaki Neo4j url'ini(ki muhtemeln satıdaki url doğrudur) , username ve password bilgilerini kontrol edin.

dotnet run komutu ile kodu çalıştırabilirsiniz. Terminal çıktınız class - metod - sp leri node olarak ifade edecek ve aralarındaki relationları olacak.

aşağıdaki komut ile tüm terminal çıktınızda görünen tüm class- metod - sp nodeları ve relationlar bu cypher ile gözlemlenebilir.
MATCH (a)-[r]->(b)
RETURN a, r, b

delete Cypherı :
MATCH (n) DETACH DELETE n
Terminali her çalıştırdığınızda var olan graph üzerine ekleme yapar. delete komutunu çalıştırarak her seferinde temiz graph ile başlayabilirsiniz
